using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.DataversePlugins;

/// <summary>
/// Async Dataverse plug-in that dispatches SVT processing when the tracking row changes to a requested state.
/// Register this on Update for <c>voa_svtprocessing</c> with filtering on <c>voa_dispatchstate</c>.
/// </summary>
public sealed class SvtDispatchPlugin : IPlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _functionUrl;
    private readonly TimeSpan _timeout;

    public SvtDispatchPlugin(string unsecureConfig, string secureConfig)
    {
        var settings = ParseSettings(unsecureConfig);
        _functionUrl = settings.FunctionUrl ?? throw new InvalidPluginExecutionException("SVT dispatch function URL is required in plugin configuration.");
        _timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 30);
    }

    public void Execute(IServiceProvider serviceProvider)
    {
        var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService))!;
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext))!;

        // This plug-in is intentionally narrow: it only reacts to Update on the SVT tracking table.
        if (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Keep the trigger isolated to the SVT tracking table so bulk paths never invoke this plug-in.
        if (!string.Equals(context.PrimaryEntityName, SvtProcessingConstants.EntityNames.SvtProcessing, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Target contains only the changed columns for this update.
        if (!context.InputParameters.TryGetValue("Target", out var targetObj) || targetObj is not Entity target)
        {
            tracingService.Trace("SVT dispatch plug-in skipped because the target entity was missing.");
            return;
        }

        // Only dispatch when the trigger column actually changes.
        if (!target.Attributes.Contains(SvtProcessingConstants.Fields.DispatchState))
        {
            tracingService.Trace("SVT dispatch plug-in skipped because dispatch state was not changed.");
            return;
        }

        var dispatchState = target.GetAttributeValue<OptionSetValue>(SvtProcessingConstants.Fields.DispatchState)?.Value;
        // The plug-in only fires for the explicit trigger states. Other status updates are ignored.
        if (dispatchState is not (SvtProcessingConstants.DispatchStateCodes.Requested or SvtProcessingConstants.DispatchStateCodes.ReRequested))
        {
            tracingService.Trace("SVT dispatch plug-in skipped because dispatch state {0} is not a trigger state.", dispatchState);
            return;
        }

        // Correlation id is used for traceability and idempotency across retries.
        var svtProcessingId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
        var correlationId = target.GetAttributeValue<string>(SvtProcessingConstants.Fields.CorrelationId) ?? context.CorrelationId.ToString();

        var payload = new BulkDataRouteDecisionRequest
        {
            SvtProcessingId = svtProcessingId,
            CorrelationId = correlationId,
        };

        // The Azure Function does the actual request/job creation; the plug-in is just the dispatcher.
        var requestJson = JsonSerializer.Serialize(payload, JsonOptions);
        using var httpClient = new HttpClient
        {
            Timeout = _timeout,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _functionUrl)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("x-correlation-id", correlationId);

        tracingService.Trace("Dispatching SVT processing request. SvtProcessingId={0}, CorrelationId={1}", svtProcessingId, correlationId);

        // Keep the call synchronous here so the async plug-in can surface dispatch failures immediately.
        using var response = httpClient.SendAsync(request).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            tracingService.Trace(
                "SVT dispatch function returned non-success status {0}. Response={1}",
                response.StatusCode,
                responseText);

            throw new InvalidPluginExecutionException(
                $"SVT dispatch function call failed with HTTP {(int)response.StatusCode}.");
        }

        tracingService.Trace(
            "SVT dispatch plug-in completed successfully. SvtProcessingId={0}, CorrelationId={1}",
            svtProcessingId,
            correlationId);
    }

    private sealed class PluginSettings
    {
        // FunctionUrl is configured through the plug-in registration string.
        public string? FunctionUrl { get; set; }

        // Keep the plug-in timeout bounded so a stalled HTTP call does not hang dispatch indefinitely.
        public int TimeoutSeconds { get; set; } = 30;
    }

    private static PluginSettings ParseSettings(string config)
    {
        // Support either a direct URL or a semicolon-delimited key/value config string.
        if (string.IsNullOrWhiteSpace(config))
        {
            return new PluginSettings();
        }

        var settings = new PluginSettings();
        var trimmed = config.Trim();

        if (!trimmed.Contains('=') && Uri.TryCreate(trimmed, UriKind.Absolute, out var directUri))
        {
            settings.FunctionUrl = directUri.ToString();
            return settings;
        }

        foreach (var segment in trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            switch (parts[0].ToLowerInvariant())
            {
                case "functionurl":
                    settings.FunctionUrl = parts[1];
                    break;
                case "timeoutseconds":
                    if (int.TryParse(parts[1], out var timeoutSeconds))
                    {
                        settings.TimeoutSeconds = timeoutSeconds;
                    }
                    break;
            }
        }

        return settings;
    }
}
