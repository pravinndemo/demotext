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
    private const string ItemEntityName = "voa_bulkingestionitem";
    private const string ItemParentLookupColumn = "voa_parentbulkingestion";
    private const string ItemValidationStatusColumn = "voa_validationstatus";
    private const string ItemValidationFailureReasonColumn = "voa_validationfailurereason";
    private const string ItemProcessingStageColumn = "voa_processingstage";
    private const string ItemProcessingTimestampColumn = "voa_processingtimestamp";
    private const string ItemProcessingAttemptCountColumn = "voa_processingattemptcount";
    private const string ItemLockedForProcessingColumn = "voa_lockedforprocessing";
    private const string ItemCanReprocessColumn = "voa_canreprocess";
    private const string ItemSsuIdColumn = "voa_ssuid";

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
        var finalStatus = await DetermineFinalIngestionStatusAsync(ingestionResult);
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
                await UpdateItemProcessingStateAsync(
                    itemId: item.Id,
                    stageCode: StatusCodes.StageValidation,
                    lockForProcessing: true,
                    incrementAttempt: true,
                    errorMessage: null,
                    canReprocess: true);

                var ssuId = item.GetAttributeValue<string>(ItemSsuIdColumn)?.Trim() ?? string.Empty;
                var parentIngestionId = item.GetAttributeValue<EntityReference>(ItemParentLookupColumn)?.Id ?? Guid.Empty;

                if (parentIngestionId != Guid.Empty &&
                    !string.IsNullOrWhiteSpace(ssuId) &&
                    await SsuIdExistsInOtherBatchesAsync(parentIngestionId, ssuId))
                {
                    await TryMarkItemAsFailedAsync(
                        itemId: item.Id,
                        stageCode: StatusCodes.StageValidation,
                        errorMessage: "ERR_DUP_SSU_OTHER_BATCH: SSU ID already exists in another batch.",
                        canReprocess: false);
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
        else
        {
            foreach (var item in batch)
            {
                await UpdateItemProcessingStateAsync(
                    itemId: item.Id,
                    stageCode: StatusCodes.StageRequestCreation,
                    lockForProcessing: true,
                    incrementAttempt: true,
                    errorMessage: null,
                    canReprocess: true);
            }
        }

        foreach (var item in itemsToProcess)
        {
            await UpdateItemProcessingStateAsync(
                itemId: item.Id,
                stageCode: StatusCodes.StageRequestCreation,
                lockForProcessing: true,
                incrementAttempt: false,
                errorMessage: null,
                canReprocess: true);
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
        var query = new QueryExpression(ItemEntityName)
        {
            ColumnSet = new ColumnSet(false),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 1 },
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression(ItemSsuIdColumn, ConditionOperator.Equal, ssuId),
                    new ConditionExpression(ItemParentLookupColumn, ConditionOperator.NotEqual, parentIngestionId),
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
            await UpdateItemProcessingStateAsync(
                itemId: item.Id,
                stageCode: StatusCodes.StageRequestCreation,
                lockForProcessing: true,
                incrementAttempt: false,
                errorMessage: null,
                canReprocess: true);

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
            var isTransient = IsTransientException(ex);
            await TryMarkItemAsFailedAsync(
                itemId: item.Id,
                stageCode: StatusCodes.StageRequestCreation,
                errorMessage: ex.Message,
                canReprocess: isTransient);

            _logger.LogWarning(
                "RetrySingleItem exhausted for {ItemId}. ClassifiedTransient={IsTransient}. Error={Error}",
                item.Id,
                isTransient,
                ex.Message);

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
        var update = new Entity(ItemEntityName, item.Id);
        update[ItemValidationStatusColumn] = new OptionSetValue(StatusCodes.Processed);
        update[ItemProcessingStageColumn] = new OptionSetValue(StatusCodes.StageCompleted);
        update[ItemProcessingTimestampColumn] = DateTime.UtcNow;
        update[ItemValidationFailureReasonColumn] = string.Empty;
        update[ItemLockedForProcessingColumn] = false;
        update[ItemCanReprocessColumn] = false;

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
        var status = await DetermineFinalIngestionStatusAsync(result);

        var parent = new Entity("voa_bulkingestion", result.IngestionId);
        parent["statuscode"] = new OptionSetValue(status);

        await RetryAsync(async () =>
        {
            await _crmService.UpdateAsync(parent);
            return true;
        }, $"Finalise {result.IngestionId}", MaxRetries);

        _logger.LogInformation($"Completed Ingestion {result.IngestionId}");
    }

    private async Task<int> DetermineFinalIngestionStatusAsync(IngestionProcessingResult result)
    {
        if (result.FailureCount == 0)
        {
            return StatusCodes.Completed;
        }

        if (await HasReprocessableFailuresAsync(result.IngestionId))
        {
            return StatusCodes.Delayed;
        }

        return result.SuccessCount == 0
            ? StatusCodes.Failed
            : StatusCodes.PartialSuccess;
    }

    private async Task<bool> HasReprocessableFailuresAsync(Guid ingestionId)
    {
        var query = new QueryExpression(ItemEntityName)
        {
            ColumnSet = new ColumnSet(false),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 1 },
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression(ItemParentLookupColumn, ConditionOperator.Equal, ingestionId),
                    new ConditionExpression(ItemValidationStatusColumn, ConditionOperator.Equal, StatusCodes.ItemFailed),
                    new ConditionExpression(ItemCanReprocessColumn, ConditionOperator.Equal, true),
                }
            }
        };

        var response = await _crmService.RetrieveMultipleAsync(query);
        return response.Entities.Count > 0;
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
        var query = new QueryExpression(ItemEntityName)
        {
            ColumnSet = new ColumnSet(true)
        };

        query.Criteria.AddCondition(ItemParentLookupColumn, ConditionOperator.Equal, ingestionId);
        query.Criteria.AddCondition(ItemValidationStatusColumn, ConditionOperator.Equal, StatusCodes.Valid);

        var result = await _crmService.RetrieveMultipleAsync(query);
        return result.Entities.ToList();
    }

    private async Task UpdateIngestionStatusAsync(Guid id, int status)
    {
        var entity = new Entity("voa_bulkingestion", id);
        entity["statuscode"] = new OptionSetValue(status);

        await RetryAsync(async () =>
        {
            await _crmService.UpdateAsync(entity);
            return true;
        }, $"UpdateStatus {id}", MaxRetries);
    }

    private async Task TryMarkItemAsFailedAsync(Guid itemId, int stageCode, string errorMessage, bool canReprocess)
    {
        try
        {
            var entity = new Entity(ItemEntityName, itemId)
            {
                [ItemValidationStatusColumn] = new OptionSetValue(StatusCodes.ItemFailed),
                [ItemProcessingStageColumn] = new OptionSetValue(stageCode),
                [ItemProcessingTimestampColumn] = DateTime.UtcNow,
                [ItemValidationFailureReasonColumn] = errorMessage,
                [ItemCanReprocessColumn] = canReprocess,
                [ItemLockedForProcessingColumn] = false,
            };

            await _crmService.UpdateAsync(entity);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not mark item {itemId} as failed: {ex.Message}");
        }
    }

    private async Task UpdateItemProcessingStateAsync(
        Guid itemId,
        int stageCode,
        bool lockForProcessing,
        bool incrementAttempt,
        string? errorMessage,
        bool canReprocess)
    {
        try
        {
            var update = new Entity(ItemEntityName, itemId)
            {
                [ItemProcessingStageColumn] = new OptionSetValue(stageCode),
                [ItemProcessingTimestampColumn] = DateTime.UtcNow,
                [ItemLockedForProcessingColumn] = lockForProcessing,
                [ItemCanReprocessColumn] = canReprocess,
            };

            if (errorMessage is not null)
            {
                update[ItemValidationFailureReasonColumn] = errorMessage;
            }

            if (incrementAttempt)
            {
                var item = await _crmService.RetrieveAsync(ItemEntityName, itemId, new ColumnSet(ItemProcessingAttemptCountColumn));
                var attemptCount = item.GetAttributeValue<int?>(ItemProcessingAttemptCountColumn) ?? 0;
                update[ItemProcessingAttemptCountColumn] = attemptCount + 1;
            }

            await _crmService.UpdateAsync(update);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update processing state for item {ItemId}", itemId);
        }
    }

    private static bool IsTransientException(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException ||
                current is TaskCanceledException ||
                current is HttpRequestException)
            {
                return true;
            }

            var message = current.Message ?? string.Empty;
            if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("throttle", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("503", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("connection", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}


