using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Activities;

public class BulkIngestionProcessor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOrganizationServiceAsync2 _crmService;
    private readonly ILogger _logger;

    private const int BatchSize = 1000;
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 500;

    public BulkIngestionProcessor(
        IHttpClientFactory httpClientFactory,
        IOrganizationServiceAsync2 crmService,
        ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _crmService = crmService;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var runSw = Stopwatch.StartNew();
        var processingRunId = Guid.NewGuid().ToString("N");
        List<Entity> submittedIngestions = await RetrieveSubmittedIngestionsAsync();

        if (!submittedIngestions.Any())
        {
            _logger.LogInformation("No submitted ingestion records found.");
            return;
        }

        _logger.LogInformation($"Found {submittedIngestions.Count} submitted Bulk Ingestion(s) to process.");

        var processedIngestions = 0;
        var failedIngestions = 0;

        foreach (Entity ingestion in submittedIngestions)
        {
            try
            {
                await ProcessSingleIngestionAsync(ingestion, processingRunId);
                processedIngestions++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing BulkIngestion [{Id}]", ingestion.Id);
                failedIngestions++;
            }
        }

        runSw.Stop();
        _logger.LogInformation(
            "Performance.TimerRunSummary ProcessingRunId={ProcessingRunId} SubmittedIngestions={SubmittedIngestions} ProcessedIngestions={ProcessedIngestions} FailedIngestions={FailedIngestions} TotalElapsedMs={TotalElapsedMs}",
            processingRunId,
            submittedIngestions.Count,
            processedIngestions,
            failedIngestions,
            runSw.ElapsedMilliseconds);
    }

    private async Task ProcessSingleIngestionAsync(Entity ingestion, string processingRunId)
    {
        var ingestionSw = Stopwatch.StartNew();
        _logger.LogInformation("Processing Bulk Ingestion [{Id}] ProcessingRunId={ProcessingRunId}", ingestion.Id, processingRunId);

        // Note: Status remains Queued (358800003) until processing completes and final state is set
        // await UpdateIngestionStatusAsync(ingestion.Id, StatusCodes.Processing);

        List<Entity> validItems = await RetrieveValidItemsAsync(ingestion.Id);

        if (!validItems.Any())
        {
            _logger.LogWarning("BulkIngestion [{Id}] has no valid items. Marking as Failed.", ingestion.Id);
            await UpdateIngestionStatusAsync(ingestion.Id, StatusCodes.Failed);
            ingestionSw.Stop();
            _logger.LogInformation(
                "Performance.TimerIngestionSummary ProcessingRunId={ProcessingRunId} IngestionId={IngestionId} ValidItems=0 SuccessCount=0 FailureCount=0 Batches=0 FinalStatus={FinalStatus} TotalElapsedMs={TotalElapsedMs}",
                processingRunId,
                ingestion.Id,
                StatusCodes.Failed,
                ingestionSw.ElapsedMilliseconds);
            return;
        }

        var ingestionResult = new IngestionProcessingResult
        {
            IngestionId = ingestion.Id,
            TotalItems = validItems.Count
        };

        for (int batchStart = 0; batchStart < validItems.Count; batchStart += BatchSize)
        {
            List<Entity> batch = validItems.Skip(batchStart).Take(BatchSize).ToList();
            int batchNumber = (batchStart / BatchSize) + 1;

            _logger.LogInformation($"Batch {batchNumber} | {batch.Count} items");

            List<ItemProcessingResult> batchResults =
                await ProcessBatchWithRetryAsync(batch, batchNumber, processingRunId);

            foreach (var result in batchResults)
            {
                if (result.Success)
                    ingestionResult.SuccessCount++;
                else
                {
                    ingestionResult.FailureCount++;
                    ingestionResult.Errors.Add(result.Error ?? "Unknown error");
                }
            }
        }

        await FinaliseIngestionAsync(ingestionResult);

        ingestionSw.Stop();
        var finalStatus =
            ingestionResult.FailureCount == 0 ? StatusCodes.Completed :
            ingestionResult.SuccessCount == 0 ? StatusCodes.Failed :
            StatusCodes.PartialSuccess;
        var batchCount = (int)Math.Ceiling(validItems.Count / (double)BatchSize);
        _logger.LogInformation(
            "Performance.TimerIngestionSummary ProcessingRunId={ProcessingRunId} IngestionId={IngestionId} ValidItems={ValidItems} SuccessCount={SuccessCount} FailureCount={FailureCount} Batches={BatchCount} FinalStatus={FinalStatus} TotalElapsedMs={TotalElapsedMs}",
            processingRunId,
            ingestion.Id,
            validItems.Count,
            ingestionResult.SuccessCount,
            ingestionResult.FailureCount,
            batchCount,
            finalStatus,
            ingestionSw.ElapsedMilliseconds);
    }

    private async Task<List<ItemProcessingResult>> ProcessBatchWithRetryAsync(
        List<Entity> batch, int batchNumber, string processingRunId)
    {
        var batchSw = Stopwatch.StartNew();
        var results = new List<ItemProcessingResult>();

        var itemsToProcess = batch;
        var checkCrossBatchDuplicates = GetBooleanFlag("BulkIngestionCheckCrossBatchDuplicates", false);
        var crossBatchRejectedCount = 0;

        if (checkCrossBatchDuplicates)
        {
            itemsToProcess = new List<Entity>(batch.Count);

            foreach (var item in batch)
            {
                var ssuId = item.GetAttributeValue<string>("voa_ssuid")?.Trim() ?? string.Empty;
                var parentIngestionId = item.GetAttributeValue<EntityReference>("voa_parentingestion")?.Id ?? Guid.Empty;

                if (parentIngestionId != Guid.Empty &&
                    !string.IsNullOrWhiteSpace(ssuId) &&
                    await SsuIdExistsInOtherBatchesAsync(parentIngestionId, ssuId))
                {
                    await TryMarkItemAsFailedAsync(item.Id);
                    crossBatchRejectedCount++;

                    results.Add(new ItemProcessingResult
                    {
                        ItemId = item.Id,
                        Success = false,
                        Error = "ERR_DUP_SSU_OTHER_BATCH: SSU ID already exists in another batch."
                    });

                    continue;
                }

                itemsToProcess.Add(item);
            }
        }

        if (!itemsToProcess.Any())
        {
            batchSw.Stop();
            _logger.LogInformation(
                "Performance.TimerBatchSummary ProcessingRunId={ProcessingRunId} BatchNumber={BatchNumber} InputItems={InputItems} EligibleItems=0 CrossBatchRejected={CrossBatchRejected} SuccessCount={SuccessCount} FailureCount={FailureCount} ElapsedMs={ElapsedMs}",
                processingRunId,
                batchNumber,
                batch.Count,
                crossBatchRejectedCount,
                results.Count(result => result.Success),
                results.Count(result => !result.Success),
                batchSw.ElapsedMilliseconds);
            return results;
        }

        ExecuteMultipleResponse response = await RetryAsync(
            () => ExecuteBatchAsync(itemsToProcess),
            $"Batch {batchNumber}",
            MaxRetries);

        var failedItems = new List<Entity>();

        foreach (var item in response.Responses)
        {
            Entity entity = itemsToProcess[item.RequestIndex];

            if (item.Fault == null)
            {
                results.Add(new ItemProcessingResult
                {
                    ItemId = entity.Id,
                    Success = true
                });
            }
            else
            {
                _logger.LogWarning($"Item {entity.Id} failed - retrying");
                failedItems.Add(entity);
            }
        }

        if (failedItems.Any())
        {
            var retryTasks = failedItems.Select(RetrySingleItemAsync);
            results.AddRange(await Task.WhenAll(retryTasks));
        }

        batchSw.Stop();
        _logger.LogInformation(
            "Performance.TimerBatchSummary ProcessingRunId={ProcessingRunId} BatchNumber={BatchNumber} InputItems={InputItems} EligibleItems={EligibleItems} CrossBatchRejected={CrossBatchRejected} SuccessCount={SuccessCount} FailureCount={FailureCount} ElapsedMs={ElapsedMs}",
            processingRunId,
            batchNumber,
            batch.Count,
            itemsToProcess.Count,
            crossBatchRejectedCount,
            results.Count(result => result.Success),
            results.Count(result => !result.Success),
            batchSw.ElapsedMilliseconds);

        return results;
    }

    private async Task<bool> SsuIdExistsInOtherBatchesAsync(Guid parentIngestionId, string ssuId)
    {
        var query = new QueryExpression("voa_bulkingestionitem")
        {
            ColumnSet = new ColumnSet(false),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 1 },
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("voa_ssuid", ConditionOperator.Equal, ssuId),
                    new ConditionExpression("voa_parentingestion", ConditionOperator.NotEqual, parentIngestionId),
                }
            }
        };

        var result = await _crmService.RetrieveMultipleAsync(query);
        return result.Entities.Count > 0;
    }

    private static bool GetBooleanFlag(string key, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    private async Task<ItemProcessingResult> RetrySingleItemAsync(Entity item)
    {
        try
        {
            await RetryAsync(
                async () =>
                {
                    await _crmService.ExecuteAsync(BuildItemRequest(item));
                    return true;
                },
                $"Item {item.Id}",
                MaxRetries);

            return new ItemProcessingResult { ItemId = item.Id, Success = true };
        }
        catch (Exception ex)
        {
            await TryMarkItemAsFailedAsync(item.Id);

            return new ItemProcessingResult
            {
                ItemId = item.Id,
                Success = false,
                Error = ex.Message
            };
        }
    }

    private async Task<ExecuteMultipleResponse> ExecuteBatchAsync(List<Entity> batch)
    {
        var request = new ExecuteMultipleRequest
        {
            Settings = new ExecuteMultipleSettings
            {
                ContinueOnError = true,
                ReturnResponses = true
            },
            Requests = new OrganizationRequestCollection()
        };

        foreach (var item in batch)
        {
            request.Requests.Add(BuildItemRequest(item));
        }

        return (ExecuteMultipleResponse)
            await _crmService.ExecuteAsync(request);
    }

    private OrganizationRequest BuildItemRequest(Entity item)
    {
        var update = new Entity("voa_bulkingestionitem", item.Id);
        update["voa_itemstatus"] = new OptionSetValue(StatusCodes.Processed);

        return new UpdateRequest { Target = update };
    }

    private async Task<T> RetryAsync<T>(
        Func<Task<T>> operation,
        string operationId,
        int maxRetries)
    {
        int attempt = 0;

        while (true)
        {
            try
            {
                return await operation();
            }
            catch (Exception) when (attempt < maxRetries)
            {
                attempt++;
                int delay = BaseDelayMs * (int)Math.Pow(2, attempt - 1);

                _logger.LogWarning($"Retry {operationId} attempt {attempt} - waiting {delay}ms");

                await Task.Delay(delay);
            }
        }
    }

    private async Task FinaliseIngestionAsync(IngestionProcessingResult result)
    {
        int status =
            result.FailureCount == 0 ? StatusCodes.Completed :
            result.SuccessCount == 0 ? StatusCodes.Failed :
            StatusCodes.PartialSuccess;

        var parent = new Entity("voa_bulkingestion", result.IngestionId);
        parent["voa_ingestionstatus"] = new OptionSetValue(status);

        await RetryAsync(async () =>
        {
            await _crmService.UpdateAsync(parent);
            return true;
        }, $"Finalise {result.IngestionId}", MaxRetries);

        _logger.LogInformation($"Completed Ingestion {result.IngestionId}");
    }

    private async Task<List<Entity>> RetrieveSubmittedIngestionsAsync()
    {
        var query = new QueryExpression("voa_bulkingestion")
        {
            ColumnSet = new ColumnSet("statuscode")
        };

        query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, StatusCodes.Queued);

        var result = await _crmService.RetrieveMultipleAsync(query);
        return result.Entities.ToList();
    }

    private async Task<List<Entity>> RetrieveValidItemsAsync(Guid ingestionId)
    {
        var query = new QueryExpression("voa_bulkingestionitem")
        {
            ColumnSet = new ColumnSet(true)
        };

        query.Criteria.AddCondition("voa_parentingestion", ConditionOperator.Equal, ingestionId);
        query.Criteria.AddCondition("voa_itemstatus", ConditionOperator.Equal, StatusCodes.Valid);

        var result = await _crmService.RetrieveMultipleAsync(query);
        return result.Entities.ToList();
    }

    private async Task UpdateIngestionStatusAsync(Guid id, int status)
    {
        var entity = new Entity("voa_bulkingestion", id);
        entity["voa_ingestionstatus"] = new OptionSetValue(status);

        await RetryAsync(async () =>
        {
            await _crmService.UpdateAsync(entity);
            return true;
        }, $"UpdateStatus {id}", MaxRetries);
    }

    private async Task TryMarkItemAsFailedAsync(Guid itemId)
    {
        try
        {
            var entity = new Entity("voa_bulkingestionitem", itemId);
            entity["voa_itemstatus"] = new OptionSetValue(StatusCodes.ItemFailed);

            await _crmService.UpdateAsync(entity);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not mark item {itemId} as failed: {ex.Message}");
        }
    }
}


