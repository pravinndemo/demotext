using System;
using System.Net.Http;
using System.Reflection;

namespace VOA.CouncilTax.AutoProcessing.Helpers;

public static class HttpClientExtensions
{
    public static HttpClient MakeDataAccessLayerHeaders(
        this HttpClient client,
        object? tracker,
        string? caseObjectType = null,
        string? caseObjectId = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        SetHeader(client, "CorrelationId", Guid.NewGuid().ToString());
        SetHeader(client, "CaseObjectId", caseObjectId ?? GetTrackerValue(tracker, "RequestId", "JobId", "IncidentId"));
        SetHeader(client, "CaseObjectType", caseObjectType ?? InferCaseObjectType(tracker));
        SetHeader(client, "ActiveDirectoryObjectId", GetTrackerValue(tracker, "UserAADId"));
        SetHeader(client, "x-function-instanceId", GetTrackerValue(tracker, "FunctionInstanceId"));

        return client;
    }

    private static void SetHeader(HttpClient client, string name, string? value)
    {
        client.DefaultRequestHeaders.Remove(name);

        if (!string.IsNullOrWhiteSpace(value))
        {
            client.DefaultRequestHeaders.Add(name, value);
        }
    }

    private static string? GetTrackerValue(object? tracker, params string[] propertyNames)
    {
        if (tracker is null)
        {
            return null;
        }

        var trackerType = tracker.GetType();
        foreach (var propertyName in propertyNames)
        {
            var property = trackerType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            var value = property?.GetValue(tracker);
            if (value is not null)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static string InferCaseObjectType(object? tracker)
    {
        var trackerTypeName = tracker?.GetType().Name ?? string.Empty;
        return trackerTypeName.Contains("Request", StringComparison.OrdinalIgnoreCase)
            ? "voa_requestlineitem"
            : "incident";
    }
}
