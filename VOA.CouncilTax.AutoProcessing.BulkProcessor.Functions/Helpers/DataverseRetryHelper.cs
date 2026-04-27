using System;
using System.Net;
using System.ServiceModel;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;

namespace VOA.CouncilTax.AutoProcessing.Helpers;

/// <summary>
/// Reusable retry helper for transient Dataverse failures such as throttling and service unavailability.
/// </summary>
public static class DataverseRetryHelper
{
    private const int MaxRetries = 5;
    private const int BaseDelayMilliseconds = 500;

    public static async Task ExecuteWithRetryAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var retryCount = 0;
        while (true)
        {
            try
            {
                await operation();
                return;
            }
            catch (Exception ex) when (IsTransientDataverseException(ex) && retryCount < MaxRetries)
            {
                retryCount++;
                await Task.Delay(GetDelay(retryCount));
            }
        }
    }

    public static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var retryCount = 0;
        while (true)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (IsTransientDataverseException(ex) && retryCount < MaxRetries)
            {
                retryCount++;
                await Task.Delay(GetDelay(retryCount));
            }
        }
    }

    private static TimeSpan GetDelay(int retryCount)
    {
        var delayMilliseconds = BaseDelayMilliseconds * Math.Pow(2, retryCount);
        return TimeSpan.FromMilliseconds(delayMilliseconds);
    }

    private static bool IsTransientDataverseException(Exception ex)
    {
        if (ex is TimeoutException)
        {
            return true;
        }

        if (ex is FaultException<OrganizationServiceFault> faultException)
        {
            return faultException.Detail.ErrorCode == (int)HttpStatusCode.TooManyRequests ||
                   faultException.Detail.ErrorCode == (int)HttpStatusCode.RequestTimeout ||
                   faultException.Detail.ErrorCode == (int)HttpStatusCode.ServiceUnavailable ||
                   faultException.Detail.ErrorCode == (int)HttpStatusCode.GatewayTimeout;
        }

        return false;
    }
}
