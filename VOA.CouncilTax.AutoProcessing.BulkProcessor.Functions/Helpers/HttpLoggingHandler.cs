using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VOA.CouncilTax.AutoProcessing.Helpers;

// Placeholder interface kept to match the original file structure.
public interface ILogWriter
{
    void WriteFailureLog(string message, string correlationId);
    void WriteInformationLog(string message, string targetSystem, string correlationId);
    void WriteSuccessLog(string message, string correlationId);
}

/// <summary>
/// Logs outgoing HTTP requests and responses for named HttpClient calls.
/// </summary>
public sealed class HttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger<HttpLoggingHandler> _logger;

    public HttpLoggingHandler(ILogger<HttpLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var loggingScope = BuildLoggingScope(request);
        using var scope = _logger.BeginScope(loggingScope);

        var requestContent = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(requestContent))
        {
            _logger.LogInformation("Making HTTP request {Method} {RequestUri}", request.Method.Method, request.RequestUri);
        }
        else
        {
            _logger.LogInformation(
                "Making HTTP request {Method} {RequestUri}. Request content: {RequestContent}",
                request.Method.Method,
                request.RequestUri,
                requestContent);
        }

        var response = await base.SendAsync(request, cancellationToken);

        var responseContent = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        _logger.LogInformation(
            "HTTP response {StatusCode}. Response content: {ResponseContent}",
            (int)response.StatusCode,
            responseContent);

        return response;
    }

    private static Dictionary<string, object> BuildLoggingScope(HttpRequestMessage request)
    {
        var scope = new Dictionary<string, object>();

        if (request.Headers.TryGetValues("x-function-instanceId", out var functionInstanceIds))
        {
            var instanceId = functionInstanceIds.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                scope["instanceId"] = instanceId;
            }

            request.Headers.Remove("x-function-instanceId");
        }

        return scope;
    }
}
