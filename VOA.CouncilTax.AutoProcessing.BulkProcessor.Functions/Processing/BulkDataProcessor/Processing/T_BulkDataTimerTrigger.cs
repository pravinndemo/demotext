using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Activities;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Processing;

public static class TimerScheduleSettings
{
    public const string BulkIngestionTimerSchedule = "%BulkIngestionTimerSchedule%";
}

public class T_BulkDataTimerTrigger
{
    private readonly ILogger<T_BulkDataTimerTrigger> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOrganizationServiceAsync2 _crmService;

    public T_BulkDataTimerTrigger(
        ILogger<T_BulkDataTimerTrigger> logger,
        IHttpClientFactory httpClientFactory,
        IOrganizationServiceAsync2 crmService)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _crmService = crmService;
    }

    [Function("T_BulkDataTimerTrigger")]
    public async Task Run([TimerTrigger(TimerScheduleSettings.BulkIngestionTimerSchedule)] TimerInfo myTimer)
    {
        _logger.LogInformation($"T_BulkDataTimerTrigger executed at {DateTime.UtcNow}");

        var createImmediately = Environment.GetEnvironmentVariable("BulkSubmitCreateImmediately") ?? "true";
        if (bool.TryParse(createImmediately, out var createNow) && !createNow)
        {
            _logger.LogWarning(
                "BulkSubmitCreateImmediately=false detected. Current timer processor does not create request/job records, so custom upsert and customer resolution checks are not exercised in timer flow.");
        }

        if (myTimer.ScheduleStatus is not null)
        {
            _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
        }

        try
        {
            var bulkIngestionProcessor = new BulkIngestionProcessor(_httpClientFactory, _crmService, _logger);
            await bulkIngestionProcessor.RunAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in BulkDataTimerTrigger");
        }
    }
}


