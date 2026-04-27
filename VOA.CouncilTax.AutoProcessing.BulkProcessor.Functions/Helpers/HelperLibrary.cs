using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace VOA.CouncilTax.AutoProcessing.Helpers;

public static class HelperLibrary
{
    public static string Splice(this string value, int start, string replacement)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(replacement) || start < 0 || start >= value.Length)
        {
            return value;
        }

        var removeLength = Math.Min(replacement.Length, value.Length - start);
        return value.Remove(start, removeLength).Insert(start, replacement).Substring(0, value.Length);
    }

    public static async Task<bool> HasExistingInProgressInstances(
        string instanceIdPrefix,
        DurableTaskClient starter,
        string orchestrationName = "")
    {
        var query = new OrchestrationQuery
        {
            InstanceIdPrefix = instanceIdPrefix,
            Statuses = new[]
            {
                OrchestrationRuntimeStatus.Pending,
                OrchestrationRuntimeStatus.Running,
                OrchestrationRuntimeStatus.ContinuedAsNew,
            },
        };

        await foreach (var instance in starter.GetAllInstancesAsync(query))
        {
            if (string.IsNullOrWhiteSpace(orchestrationName) ||
                string.Equals(instance.Name, orchestrationName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string GetNewInstanceId(Guid guid)
    {
        return Guid.NewGuid().ToString().Splice(0, guid.ToString()[..8]);
    }

    public static string GetLowerEight(this string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 8 ? value : value[..8];
    }

    public static string GetLowerEight(this Guid guid)
    {
        return guid.ToString()[..8];
    }

    public static Guid? GetNextGuid(this List<Guid> guids, Guid? currentGuid)
    {
        if (guids is null || !currentGuid.HasValue)
        {
            return null;
        }

        var index = guids.IndexOf(currentGuid.Value);
        if (index < 0 || index + 1 >= guids.Count)
        {
            return null;
        }

        return guids[index + 1];
    }

    public static Guid ToGuid(this string guidString)
    {
        return Guid.Parse(guidString);
    }

    public static Guid? ToNullableGuid(this string guidString)
    {
        return Guid.TryParse(guidString, out var guid) ? guid : null;
    }

    public static DateOnly ToSafeDateOnly(this DateTime dateTime)
    {
        return DateOnly.FromDateTime(dateTime.ToLocalTime());
    }

    public static string EnsureMaxLength(this string? value, int length)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length > length ? value[..length] : value;
    }

    public static async Task<string> ReadAsStringNullSafe(this HttpContent? httpContent)
    {
        if (httpContent is null)
        {
            return string.Empty;
        }

        return await httpContent.ReadAsStringAsync();
    }

    public static async Task<HttpResponseMessage> ExecuteDALWithRetryAsync(
        Func<Task<HttpResponseMessage>> operation,
        Func<HttpResponseMessage, bool> shouldRetry,
        ILogger logger,
        int maxRetries = 3,
        Func<int, TimeSpan>? backoffStrategy = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(shouldRetry);
        ArgumentNullException.ThrowIfNull(logger);

        backoffStrategy ??= attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt));

        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                response = await operation();
                if (!shouldRetry(response) || attempt == maxRetries)
                {
                    return response;
                }

                logger.LogInformation("Attempt {Attempt}: retrying due to status code {StatusCode}.", attempt, response.StatusCode);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                logger.LogInformation(ex, "Attempt {Attempt}: request exception encountered, retrying.", attempt);
            }

            await Task.Delay(backoffStrategy(attempt));
        }

        return response ?? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }
}
