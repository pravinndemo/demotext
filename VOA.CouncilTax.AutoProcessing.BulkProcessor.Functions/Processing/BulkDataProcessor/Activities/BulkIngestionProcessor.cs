using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Services;
//using VOA.CouncilTax.AutoProcessing.Consequential.Constants;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Activities;

public class BulkIngestionProcessor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOrganizationServiceAsync2 _crmService;
    private readonly ILogger _logger;

    // BatchSize is configurable via environment variable BulkTimerBatchSize (default: 200, range 1-5000).
    // See README.md for documentation.
    private static int BatchSize => GetIntConfigValue("BulkTimerBatchSize", defaultValue: 200, min: 1, max: 5000);
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 500;
    private const string ItemEntityName = "voa_bulkingestionitem";
    private const string ItemParentLookupColumn = "voa_parentbulkingestion";
    private const string ItemValidationFailureReasonColumn = "voa_validationfailurereason";
    private const string ItemProcessingStageColumn = "voa_processingstage";
    private const string ItemProcessingTimestampColumn = "voa_processingtimestamp";
    private const string ItemProcessingAttemptCountColumn = "voa_processingattemptcount";
    private const string ItemLockedForProcessingColumn = "voa_lockedforprocessing";
    private const string ItemCanReprocessColumn = "voa_canreprocess";
    private const string ItemSsuIdColumn = "voa_hereditament";
    private const string ItemOwnerColumn = "ownerid";
    private const string ItemRequestLookupColumn = "voa_requestlookup";
    private const string ItemJobLookupColumn = "voa_joblookup";
    private static string ItemValidationStatusColumn =>
        Environment.GetEnvironmentVariable("BulkIngestionItemValidationStatusColumnName") ?? "voa_validationstatus";

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
        // Only parent batches in Queued or PartialSuccess are eligible for timer pickup.
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
                await TryUpdateIngestionProcessingStateAsync(
                    ingestion.Id,
                    processingStatusValue: StatusCodes.ProcessingStatusProcessing,
                    processingStartedOn: DateTime.UtcNow,
                    processedOn: null,
                    errorSummary: null);

                await ProcessSingleIngestionAsync(ingestion, processingRunId);
                await TryUpdateIngestionProcessingStateAsync(
                    ingestion.Id,
                    processingStatusValue: StatusCodes.ProcessingStatusProcessed,
                    processingStartedOn: null,
                    processedOn: DateTime.UtcNow,
                    errorSummary: null);

                processedIngestions++;
            }
            catch (Exception ex)
            {
                var finalCounts = await RefreshIngestionCountersAsync(ingestion.Id, processingRunId);
                var finalStatus = await DetermineFinalIngestionStatusAsync(ingestion.Id, null, finalCounts);

                await UpdateIngestionStatusAsync(ingestion.Id, finalStatus);

                await TryUpdateIngestionProcessingStateAsync(
                    ingestion.Id,
                    processingStatusValue: StatusCodes.ProcessingStatusFailed,
                    processingStartedOn: null,
                    processedOn: DateTime.UtcNow,
                    errorSummary: ex.Message);

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

        // The parent batch stays in its queue-facing status until all child items finish and final status is computed.

        List<Entity> validItems = await RetrieveValidItemsAsync(ingestion.Id);

        if (!validItems.Any())
        {
            var existingCounts = await RefreshIngestionCountersAsync(ingestion.Id, processingRunId);
            var existingStatus = await DetermineFinalIngestionStatusAsync(ingestion.Id, null, existingCounts);
            _logger.LogInformation(
                "BulkIngestion [{Id}] has no valid items. Finalising from existing item statuses with status {Status}.",
                ingestion.Id,
                existingStatus);
            await UpdateIngestionStatusAsync(ingestion.Id, existingStatus);
            ingestionSw.Stop();
            _logger.LogInformation(
                "Performance.TimerIngestionSummary ProcessingRunId={ProcessingRunId} IngestionId={IngestionId} ValidItems=0 SuccessCount=0 FailureCount=0 Batches=0 FinalStatus={FinalStatus} TotalElapsedMs={TotalElapsedMs}",
                processingRunId,
                ingestion.Id,
                existingStatus,
                ingestionSw.ElapsedMilliseconds);
            return;
        }

        var createRequestJobsInTimer = ShouldCreateRequestJobsInTimer();
        var timerContext = createRequestJobsInTimer
            ? await ResolveTimerCreationContextAsync(ingestion)
            : null;

        _logger.LogInformation(
            "Timer mode for ingestion {IngestionId}: CreateRequestJobs={CreateRequestJobs}",
            ingestion.Id,
            createRequestJobsInTimer);

        var ingestionResult = new IngestionProcessingResult
        {
            IngestionId = ingestion.Id,
            TotalItems = validItems.Count
        };

        // Snapshot BatchSize once so all calculations in this invocation use a consistent value.
        var batchSize = BatchSize;

        for (int batchStart = 0; batchStart < validItems.Count; batchStart += batchSize)
        {
            List<Entity> batch = validItems.Skip(batchStart).Take(batchSize).ToList();
            int batchNumber = (batchStart / batchSize) + 1;

            _logger.LogInformation($"Batch {batchNumber} | {batch.Count} items");

            List<ItemProcessingResult> batchResults = createRequestJobsInTimer
                ? await ProcessBatchWithRequestJobCreationAsync(batch, batchNumber, processingRunId, timerContext)
                : await ProcessBatchWithRetryAsync(batch, batchNumber, processingRunId);

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

        ingestionSw.Stop();
        var finalCounts = await RefreshIngestionCountersAsync(ingestion.Id, processingRunId);
        var finalStatus = await DetermineFinalIngestionStatusAsync(ingestion.Id, ingestionResult, finalCounts);
        await UpdateIngestionStatusAsync(ingestion.Id, finalStatus);
        var batchCount = (int)Math.Ceiling(validItems.Count / (double)batchSize);
        _logger.LogInformation(
            "Performance.TimerIngestionSummary ProcessingRunId={ProcessingRunId} IngestionId={IngestionId} ValidItems={ValidItems} SuccessCount={SuccessCount} FailureCount={FailureCount} TotalRows={TotalRows} ValidCount={ValidCount} InvalidCount={InvalidCount} DuplicateCount={DuplicateCount} ProcessedCount={ProcessedCount} FailedCount={FailedCount} Batches={BatchCount} FinalStatus={FinalStatus} TotalElapsedMs={TotalElapsedMs}",
            processingRunId,
            ingestion.Id,
            validItems.Count,
            ingestionResult.SuccessCount,
            ingestionResult.FailureCount,
            finalCounts?.TotalRows ?? 0,
            finalCounts?.ValidItemCount ?? 0,
            finalCounts?.InvalidItemCount ?? 0,
            finalCounts?.DuplicateItemCount ?? 0,
            finalCounts?.ProcessedItemCount ?? 0,
            finalCounts?.FailedItemCount ?? 0,
            batchCount,
            finalStatus,
            ingestionSw.ElapsedMilliseconds);
    }

    private async Task<BulkItemCounts?> RefreshIngestionCountersAsync(Guid ingestionId, string processingRunId)
    {
        try
        {
            // Recalculate from Dataverse each pass so the parent reflects whatever was actually processed or left behind.
            var query = new QueryExpression(ItemEntityName)
            {
                ColumnSet = new ColumnSet(ItemValidationStatusColumn, "voa_isduplicate"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(ItemParentLookupColumn, ConditionOperator.Equal, ingestionId),
                    }
                }
            };

            var allItems = await _crmService.RetrieveMultipleAsync(query);
            var counts = CalculateItemCounts(allItems);

            var writer = new DataverseBulkItemWriter(_crmService);
            writer.UpdateBatchCounters(ingestionId, counts);

            _logger.LogInformation(
                "Performance.TimerCounterRefresh ProcessingRunId={ProcessingRunId} IngestionId={IngestionId} TotalRows={TotalRows} ValidItems={ValidItems} InvalidItems={InvalidItems} DuplicateItems={DuplicateItems} ProcessedItems={ProcessedItems} FailedItems={FailedItems}",
                processingRunId,
                ingestionId,
                counts.TotalRows,
                counts.ValidItemCount,
                counts.InvalidItemCount,
                counts.DuplicateItemCount,
                counts.ProcessedItemCount,
                counts.FailedItemCount);

            return counts;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not refresh batch counters for ingestion {IngestionId}. ProcessingRunId={ProcessingRunId}",
                ingestionId,
                processingRunId);
            return null;
        }
    }

    private async Task<List<ItemProcessingResult>> ProcessBatchWithRequestJobCreationAsync(
        List<Entity> batch,
        int batchNumber,
        string processingRunId,
        TimerCreationContext? timerContext)
    {
        var batchSw = Stopwatch.StartNew();
        var results = new List<ItemProcessingResult>();

        if (timerContext is null)
        {
            foreach (var item in batch)
            {
                await TryMarkItemAsFailedAsync(
                    itemId: item.Id,
                    stageCode: StatusCodes.StageRequestCreation,
                    errorMessage: "TIMER_CREATION_CONTEXT_MISSING: Unable to resolve timer submit user or template settings.",
                    canReprocess: false);

                results.Add(new ItemProcessingResult
                {
                    ItemId = item.Id,
                    Success = false,
                    Error = "TIMER_CREATION_CONTEXT_MISSING"
                });
            }

            return results;
        }

        var checkCrossBatchDuplicates = GetBooleanFlag("BulkIngestionCheckCrossBatchDuplicates", false);
        var crossBatchRejectedCount = 0;
        var eligibleItems = new List<(Entity Entity, RequestJobCreateItem Payload)>(batch.Count);
        var itemAssignedTeamColumn = Environment.GetEnvironmentVariable("BulkIngestionItemAssignedTeam") ?? "voa_assignedteam";
        var itemAssignedManagerColumn = Environment.GetEnvironmentVariable("BulkIngestionItemAssignedManager") ?? "voa_assignedmanager";

        foreach (var item in batch)
        {
            await UpdateItemProcessingStateAsync(
                itemId: item.Id,
                stageCode: StatusCodes.StageRequestCreation,
                lockForProcessing: true,
                incrementAttempt: true,
                errorMessage: null,
                canReprocess: true);

            var ssuId = item.GetAttributeValue<EntityReference>(ItemSsuIdColumn)?.Id.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(ssuId))
            {
                await TryMarkItemAsFailedAsync(
                    itemId: item.Id,
                    stageCode: StatusCodes.StageRequestCreation,
                    errorMessage: "INVALID_SSU_FORMAT: SSU ID is missing.",
                    canReprocess: false);

                results.Add(new ItemProcessingResult
                {
                    ItemId = item.Id,
                    Success = false,
                    Error = "INVALID_SSU_FORMAT"
                });
                continue;
            }

            var parentIngestionId = item.GetAttributeValue<EntityReference>(ItemParentLookupColumn)?.Id ?? Guid.Empty;
            List<CrossBatchDuplicateBatchInfo> conflictingBatches = new();
            if (checkCrossBatchDuplicates &&
                parentIngestionId != Guid.Empty)
            {
                conflictingBatches = await CrossBatchDuplicateLookupService.FindConflictingBatchesAsync(
                    _crmService,
                    _logger,
                    parentIngestionId,
                    ssuId,
                    ItemEntityName,
                    ItemParentLookupColumn,
                    ItemSsuIdColumn,
                    "voa_bulkingestion");
            }

            if (conflictingBatches.Count > 0)
            {
                var duplicateMessage = CrossBatchDuplicateMessageHelper.BuildErrorMessage(conflictingBatches);
                await TryMarkItemAsFailedAsync(
                    itemId: item.Id,
                    stageCode: StatusCodes.StageValidation,
                    errorMessage: duplicateMessage,
                    canReprocess: false);

                crossBatchRejectedCount++;
                results.Add(new ItemProcessingResult
                {
                    ItemId = item.Id,
                    Success = false,
                    Error = duplicateMessage
                });
                continue;
            }
            var hereditament = item.GetAttributeValue<EntityReference>(ItemSsuIdColumn);

            eligibleItems.Add((
                item,
                new RequestJobCreateItem
                {
                    ItemId = item.Id,
                    OwnerRef = ResolveItemOwnerReference(item, itemAssignedTeamColumn, itemAssignedManagerColumn),
                    SsuId = ssuId,
                    SourceType = timerContext.SourceType,
                    SourceValue = item.GetAttributeValue<string>("voa_sourcevalue") ?? ssuId
                }));
            
        }

        if (eligibleItems.Count == 0)
        {
            batchSw.Stop();
            _logger.LogInformation(
                "Performance.TimerCreateBatchSummary ProcessingRunId={ProcessingRunId} BatchNumber={BatchNumber} InputItems={InputItems} EligibleItems=0 CrossBatchRejected={CrossBatchRejected} SuccessCount={SuccessCount} FailureCount={FailureCount} ElapsedMs={ElapsedMs}",
                processingRunId,
                batchNumber,
                batch.Count,
                crossBatchRejectedCount,
                results.Count(r => r.Success),
                results.Count(r => !r.Success),
                batchSw.ElapsedMilliseconds);
            return results;
        }

        var creationService = new RequestJobCreationService(_crmService, _logger);
        var batchCreateResult = await creationService.CreateBatchAsync(
            eligibleItems.Select(item => item.Payload),
            timerContext.SubmitUserId,
            timerContext.ComponentName,
            timerContext.SourceType,
            jobTypeId: timerContext.JobTypeId,
            createJob: timerContext.CreateJob);

        var itemUpdateRequests = new List<OrganizationRequest>(eligibleItems.Count);
        for (var index = 0; index < eligibleItems.Count; index++)
        {
            var item = eligibleItems[index].Entity;
            var outcome = index < batchCreateResult.Results.Count
                ? batchCreateResult.Results[index]
                : new RequestJobCreateResult
                {
                    Success = false,
                    ErrorCode = "MISSING_RESULT",
                    ErrorMessage = "Creation service did not return a result for this item."
                };

            if (outcome.Success)
            {
                var update = new Entity(ItemEntityName, item.Id)
                {
                    [ItemValidationStatusColumn] = new OptionSetValue(StatusCodes.Processed),
                    [ItemProcessingStageColumn] = new OptionSetValue(StatusCodes.StageCompleted),
                    [ItemProcessingTimestampColumn] = DateTime.UtcNow,
                    [ItemValidationFailureReasonColumn] = timerContext.CreateJob
                        ? "Request/job created successfully."
                        : "Request created successfully in Request Only mode.",
                    [ItemLockedForProcessingColumn] = false,
                    [ItemCanReprocessColumn] = false,
                    [ItemRequestLookupColumn] = new EntityReference(timerContext.RequestEntityName, outcome.RequestId),
                };

                if (timerContext.CreateJob && outcome.JobId != Guid.Empty)
                {
                    update[ItemJobLookupColumn] = new EntityReference(timerContext.JobEntityName, outcome.JobId);
                }

                itemUpdateRequests.Add(new UpdateRequest { Target = update });
                results.Add(new ItemProcessingResult { ItemId = item.Id, Success = true });
            }
            else
            {
                var isTransient = string.Equals(outcome.ErrorCode, "CREATION_FAILED", StringComparison.OrdinalIgnoreCase);
                await TryMarkItemAsFailedAsync(
                    itemId: item.Id,
                    stageCode: outcome.FailureStageCode ?? StatusCodes.StageRequestCreation,
                    errorMessage: $"{outcome.ErrorCode}: {outcome.ErrorMessage}",
                    canReprocess: isTransient);

                results.Add(new ItemProcessingResult
                {
                    ItemId = item.Id,
                    Success = false,
                    Error = $"{outcome.ErrorCode}: {outcome.ErrorMessage}"
                });
            }
        }

        if (itemUpdateRequests.Count > 0)
        {
            await RetryAsync(async () =>
            {
                var request = new ExecuteMultipleRequest
                {
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = true,
                        ReturnResponses = true,
                    },
                    Requests = new OrganizationRequestCollection(),
                };

                foreach (var updateRequest in itemUpdateRequests)
                {
                    request.Requests.Add(updateRequest);
                }

                await _crmService.ExecuteAsync(request);
                return true;
            },
            $"TimerCreateBatchUpdate {batchNumber}",
            MaxRetries);
        }

        batchSw.Stop();
        _logger.LogInformation(
            "Performance.TimerCreateBatchSummary ProcessingRunId={ProcessingRunId} BatchNumber={BatchNumber} InputItems={InputItems} EligibleItems={EligibleItems} CrossBatchRejected={CrossBatchRejected} SuccessCount={SuccessCount} FailureCount={FailureCount} ElapsedMs={ElapsedMs}",
            processingRunId,
            batchNumber,
            batch.Count,
            eligibleItems.Count,
            crossBatchRejectedCount,
            results.Count(r => r.Success),
            results.Count(r => !r.Success),
            batchSw.ElapsedMilliseconds);

        return results;
    }

    private async Task<List<ItemProcessingResult>> ProcessBatchWithRetryAsync(
        List<Entity> batch, int batchNumber, string processingRunId)
    {
        // Start a stopwatch to measure total time spent on this batch.
        // e.g. Batch 1 with 5 items — we want to know how long the whole thing took.
        var batchSw = Stopwatch.StartNew();

        // This list accumulates the outcome of every item in the batch (success or failure).
        // Returned at the end so the caller can tally SuccessCount / FailureCount on the ingestion.
        var results = new List<ItemProcessingResult>();

        // By default, all items in the batch are eligible for processing.
        // e.g. batch = [Item-A, Item-B, Item-C, Item-D, Item-E] → itemsToProcess points to the same list.
        var itemsToProcess = batch;

        // Read env var "BulkIngestionCheckCrossBatchDuplicates". Defaults to false if not set.
        // When true, each item is checked against other batches for a duplicate SSU ID before processing.
        var checkCrossBatchDuplicates = GetBooleanFlag("BulkIngestionCheckCrossBatchDuplicates", false);

        // Tracks how many items were rejected as cross-batch duplicates — used in the performance log.
        var crossBatchRejectedCount = 0;

        if (checkCrossBatchDuplicates)
        {
            // Reset itemsToProcess to a new empty list — items must pass the duplicate check to be added.
            // e.g. batch has 5 items; we start with 0 eligible and build up as each item is inspected.
            itemsToProcess = new List<Entity>(batch.Count);

            foreach (var item in batch)
            {
                // Lock this item in Dataverse immediately so no other timer run picks it up concurrently.
                // Stage → StageValidation, incrementAttempt = true (this counts as an attempt).
                // e.g. Item-A: voa_processingstage = StageValidation, voa_lockedforprocessing = true, voa_processingattemptcount = 1
                await UpdateItemProcessingStateAsync(
                    itemId: item.Id,
                    stageCode: StatusCodes.StageValidation,
                    lockForProcessing: true,
                    incrementAttempt: true,
                    errorMessage: null,
                    canReprocess: true);

                // Read the item's SSU ID and parent ingestion ID from the already-fetched entity attributes.
                // Trim handles any whitespace. Falls back to empty/Guid.Empty if the attribute is null.
                // e.g. Item-A: ssuId = "SSU-001", parentIngestionId = Guid("abc-123")
                var ssuId = item.GetAttributeValue<string>(ItemSsuIdColumn)?.Trim() ?? string.Empty;
                var parentIngestionId = item.GetAttributeValue<EntityReference>(ItemParentLookupColumn)?.Id ?? Guid.Empty;

                // Three-part duplicate guard — Dataverse is only queried if the first two conditions pass:
                //   1. parentIngestionId != Guid.Empty  →  the parent lookup is populated
                //   2. ssuId is not blank               →  there is something to check
                //   3. SsuIdExistsInOtherBatchesAsync   →  same SSU ID exists under a DIFFERENT parent ingestion
                // e.g. Item-B: ssuId = "SSU-002" is found under a different parent → all three = true → rejected
                List<CrossBatchDuplicateBatchInfo> conflictingBatches = new();
                if (parentIngestionId != Guid.Empty &&
                    !string.IsNullOrWhiteSpace(ssuId))
                {
                    conflictingBatches = await CrossBatchDuplicateLookupService.FindConflictingBatchesAsync(
                        _crmService,
                        _logger,
                        parentIngestionId,
                        ssuId,
                        ItemEntityName,
                        ItemParentLookupColumn,
                        ItemSsuIdColumn,
                        "voa_bulkingestion");
                }

                if (conflictingBatches.Count > 0)
                {
                    var duplicateMessage = CrossBatchDuplicateMessageHelper.BuildErrorMessage(conflictingBatches);

                    // Mark the item as permanently failed in Dataverse.
                    // canReprocess = false because this is a data integrity issue, not a transient error.
                    // e.g. Item-B: voa_validationstatus = ItemFailed, voa_canreprocess = false
                    await TryMarkItemAsFailedAsync(
                        itemId: item.Id,
                        stageCode: StatusCodes.StageValidation,
                        errorMessage: duplicateMessage,
                        canReprocess: false);
                    crossBatchRejectedCount++;

                    // Record the failure in results so the caller knows this item failed.
                    // e.g. results now contains the batch conflict details for Item-B.
                    results.Add(new ItemProcessingResult
                    {
                        ItemId = item.Id,
                        Success = false,
                        Error = duplicateMessage
                    });

                    // Skip to the next item — this one does NOT get added to itemsToProcess.
                    continue;
                }

                // Duplicate check passed — this item is safe to process.
                // e.g. Item-A, Item-C, Item-D, Item-E all pass → itemsToProcess = [Item-A, Item-C, Item-D, Item-E]
                itemsToProcess.Add(item);
            }

            // Transition all surviving items from StageValidation → StageRequestCreation.
            // incrementAttempt = false — the attempt was already counted in the StageValidation write above.
            // e.g. Item-A, Item-C, Item-D, Item-E: voa_processingstage = StageRequestCreation
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
        }
        else
        {
            // Flag is OFF — skip the duplicate check entirely.
            // Lock all items and set stage to StageRequestCreation straight away.
            // e.g. all 5 items: voa_processingstage = StageRequestCreation, voa_lockedforprocessing = true, attempt count +1
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

        // If every item was rejected as a cross-batch duplicate, there is nothing left to send to Dataverse.
        // Log the summary and return early with just the rejection results.
        // e.g. if all 5 items had duplicate SSU IDs, itemsToProcess is empty → return here.
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

        // Send all eligible items to Dataverse in a single ExecuteMultipleRequest.
        // ContinueOnError = true means individual item faults do NOT abort the whole batch.
        // The whole call is wrapped in RetryAsync — if Dataverse throws (e.g. 503), it retries up to 3 times
        // with exponential backoff: 500ms → 1000ms → 2000ms.
        // e.g. 4 items → one network call containing 4 UpdateRequests.
        ExecuteMultipleResponse response = await RetryAsync(
            () => ExecuteBatchAsync(itemsToProcess),
            $"Batch {batchNumber}",
            MaxRetries);

        // Collect items that had individual faults in the batch response for a second-chance retry.
        var failedItems = new List<Entity>();

        foreach (var item in response.Responses)
        {
            // Match the response back to the original entity using the request index.
            // e.g. response index 0 → Item-A, index 1 → Item-C, etc.
            Entity entity = itemsToProcess[item.RequestIndex];

            if (item.Fault == null)
            {
                // Dataverse accepted this update — record as success.
                // e.g. Item-A: voa_validationstatus = Processed, voa_processingstage = StageCompleted
                results.Add(new ItemProcessingResult
                {
                    ItemId = entity.Id,
                    Success = true
                });
            }
            else
            {
                // Dataverse returned a fault for this specific item.
                // Do not give up yet — queue it for an individual retry.
                // e.g. Item-C failed in the batch → added to failedItems for RetrySingleItemAsync.
                _logger.LogWarning($"Item {entity.Id} failed - retrying");
                failedItems.Add(entity);
            }
        }

        if (failedItems.Any())
        {
            // Retry each failed item individually with bounded concurrency to avoid
            // hammering Dataverse when many items fail simultaneously.
            // BulkSingleItemRetryMaxConcurrency controls max parallel retries (default: 10, range 1-100).
            var maxRetryConcurrency = GetIntConfigValue("BulkSingleItemRetryMaxConcurrency", defaultValue: 10, min: 1, max: 100);
            using var retrySemaphore = new SemaphoreSlim(maxRetryConcurrency);

            var retryTasks = failedItems.Select(async item =>
            {
                await retrySemaphore.WaitAsync();
                try
                {
                    return await RetrySingleItemAsync(item);
                }
                finally
                {
                    retrySemaphore.Release();
                }
            });

            results.AddRange(await Task.WhenAll(retryTasks));
        }

        // Log the final batch summary with all counters for observability/performance tracking.
        // e.g. InputItems=5 EligibleItems=4 CrossBatchRejected=1 SuccessCount=4 FailureCount=1 ElapsedMs=1234
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

        // Return all results (successes + failures) to ProcessSingleIngestionAsync,
        // which uses them to update SuccessCount / FailureCount on the parent ingestion record.
        return results;
    }

    private static bool GetBooleanFlag(string key, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    /// <summary>
    /// Reads an integer environment variable and clamps it to [<paramref name="min"/>, <paramref name="max"/>].
    /// Returns <paramref name="defaultValue"/> when the value is absent, unparseable, or out of range.
    /// </summary>
    private static int GetIntConfigValue(string key, int defaultValue, int min = 1, int max = int.MaxValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (raw is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, out var parsed) || parsed < min || parsed > max)
        {
            return defaultValue;
        }

        return parsed;
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

    private async Task<int> DetermineFinalIngestionStatusAsync(
        Guid ingestionId,
        IngestionProcessingResult? result,
        BulkItemCounts? counts)
    {
        if (counts is null)
        {
            return result is null
                ? StatusCodes.Failed
                : await DetermineFinalStatusFromExistingItemsAsync(ingestionId);
        }

        var hasRemainingValidItems = counts.ValidItemCount > 0;

        if (counts.ProcessedItemCount == 0)
        {
            if (await HasReprocessableFailuresAsync(ingestionId))
            {
                return StatusCodes.PartialSuccess;
            }

            return hasRemainingValidItems || counts.InvalidItemCount > 0 || counts.DuplicateItemCount > 0 || counts.FailedItemCount > 0
                ? StatusCodes.Failed
                : StatusCodes.Completed;
        }

        if (await HasReprocessableFailuresAsync(ingestionId))
        {
            return StatusCodes.PartialSuccess;
        }

        // Any leftover Valid, Invalid, Duplicate, or Failed rows means the batch is not fully complete yet.
        if (hasRemainingValidItems || counts.InvalidItemCount > 0 || counts.DuplicateItemCount > 0 || counts.FailedItemCount > 0)
        {
            return StatusCodes.PartialSuccess;
        }

        return StatusCodes.Completed;
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
            ColumnSet = new ColumnSet("statuscode", "ownerid", "createdby", "voa_processingjobtype", "voa_template")
        };

        // The timer is re-entrant: it continues batches that are still queued or partially complete.
        query.Criteria.FilterOperator = LogicalOperator.Or;
        query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, StatusCodes.Queued);
        query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, StatusCodes.PartialSuccess);

        var result = await _crmService.RetrieveMultipleAsync(query);
        return result.Entities.ToList();
    }

    private EntityReference? ResolveItemOwnerReference(
        Entity item,
        string assignedTeamColumnName,
        string assignedManagerColumnName)
    {
        var assignedTeamRef = item.GetAttributeValue<EntityReference>(assignedTeamColumnName);
        if (assignedTeamRef is not null && assignedTeamRef.Id != Guid.Empty)
        {
            return assignedTeamRef;
        }

        var assignedManagerRef = item.GetAttributeValue<EntityReference>(assignedManagerColumnName);
        if (assignedManagerRef is not null && assignedManagerRef.Id != Guid.Empty)
        {
            return assignedManagerRef;
        }

        return item.GetAttributeValue<EntityReference>(ItemOwnerColumn);
    }

    private bool ShouldCreateRequestJobsInTimer()
    {
        // Current mode keeps bulk request/job creation on the timer path for queued batches.
        return true;
    }

    private async Task<TimerCreationContext?> ResolveTimerCreationContextAsync(Entity ingestion)
    {
        var submitUserRef = ingestion.GetAttributeValue<EntityReference>("ownerid")
            ?? ingestion.GetAttributeValue<EntityReference>("createdby");

        if (submitUserRef is null || submitUserRef.Id == Guid.Empty)
        {
            _logger.LogWarning("Could not resolve timer submit user for ingestion {IngestionId}", ingestion.Id);
            return null;
        }

        var templateSettings = await ResolveTemplateProcessingSettingsAsync(ingestion);
        if (!templateSettings.JobTypeId.HasValue || templateSettings.JobTypeId.Value == Guid.Empty)
        {
            _logger.LogWarning("Could not resolve template/header job type for ingestion {IngestionId}", ingestion.Id);
            return null;
        }

        return new TimerCreationContext(
            SubmitUserId: submitUserRef.Id.ToString(),
            ComponentName: Environment.GetEnvironmentVariable("BulkTimerComponentName") ?? "BulkTimer",
            SourceType: templateSettings.FormatLabel ?? "Bulk",
            JobTypeId: templateSettings.JobTypeId.Value,
            CreateJob: templateSettings.CreateJob,
            RequestEntityName: Environment.GetEnvironmentVariable("SvtRequestEntityLogicalName") ?? "voa_requestlineitem",
            JobEntityName: Environment.GetEnvironmentVariable("SvtJobEntityLogicalName") ?? "incident");
    }

    private async Task<int> DetermineFinalStatusFromExistingItemsAsync(Guid ingestionId)
    {
        var query = new QueryExpression(ItemEntityName)
        {
            ColumnSet = new ColumnSet(ItemValidationStatusColumn, ItemCanReprocessColumn),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression(ItemParentLookupColumn, ConditionOperator.Equal, ingestionId),
                }
            }
        };

        var allItems = await _crmService.RetrieveMultipleAsync(query);
        if (allItems.Entities.Count == 0)
        {
            return StatusCodes.Failed;
        }

        var processedCount = allItems.Entities.Count(item =>
            item.GetAttributeValue<OptionSetValue>(ItemValidationStatusColumn)?.Value == StatusCodes.Processed);
        var failedItems = allItems.Entities.Where(item =>
            item.GetAttributeValue<OptionSetValue>(ItemValidationStatusColumn)?.Value == StatusCodes.ItemFailed).ToList();

        if (failedItems.Count == 0 && processedCount > 0)
        {
            return StatusCodes.Completed;
        }

        if (failedItems.Any(item => item.GetAttributeValue<bool?>(ItemCanReprocessColumn) == true))
        {
            return StatusCodes.PartialSuccess;
        }

        if (processedCount > 0)
        {
            return StatusCodes.PartialSuccess;
        }

        return StatusCodes.Failed;
    }

    private async Task<TemplateProcessingSettings> ResolveTemplateProcessingSettingsAsync(Entity bulkProcessor)
    {
        var headerJobTypeRef = bulkProcessor.GetAttributeValue<EntityReference>("voa_processingjobtype");
        var templateRef = bulkProcessor.GetAttributeValue<EntityReference>("voa_template");

        if (templateRef is null)
        {
            return new TemplateProcessingSettings(headerJobTypeRef?.Id, true, false, null, null);
        }

        var templateEntityName = Environment.GetEnvironmentVariable("BulkIngestionTemplateEntityLogicalName") ?? "voa_bulkingestiontemplate";
        var templateJobTypeColumnName = Environment.GetEnvironmentVariable("BulkIngestionTemplateJobTypeLookupColumnName") ?? "voa_jobtypelookup";
        var templateCaseWorkModeColumnName = Environment.GetEnvironmentVariable("BulkIngestionTemplateCaseWorkModeColumnName") ?? "voa_caseworkmode";
        var templateFormatColumnName = Environment.GetEnvironmentVariable("BulkIngestionTemplateFormatColumnName") ?? "voa_format";

        try
        {
            var template = await _crmService.RetrieveAsync(
                templateEntityName,
                templateRef.Id,
                new ColumnSet(templateJobTypeColumnName, templateCaseWorkModeColumnName, templateFormatColumnName));

            var templateJobTypeRef = template.GetAttributeValue<EntityReference>(templateJobTypeColumnName);
            var caseWorkModeCode = template.GetAttributeValue<OptionSetValue>(templateCaseWorkModeColumnName)?.Value;
            var formatCode = template.GetAttributeValue<OptionSetValue>(templateFormatColumnName)?.Value;
            var formatLabel = template.FormattedValues.TryGetValue(templateFormatColumnName, out var formattedFormat)
                ? formattedFormat
                : null;
            var createJob = caseWorkModeCode != 358800000;

            return new TemplateProcessingSettings(
                templateJobTypeRef?.Id ?? headerJobTypeRef?.Id,
                createJob,
                true,
                formatLabel,
                formatCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to load template {TemplateId}; falling back to bulk header job type.",
                templateRef.Id);
            return new TemplateProcessingSettings(headerJobTypeRef?.Id, true, false, null, null);
        }
    }

    private async Task<List<Entity>> RetrieveValidItemsAsync(Guid ingestionId)
    {
        var query = new QueryExpression(ItemEntityName)
        {
            ColumnSet = new ColumnSet(true)
        };

        // Each timer pass only consumes rows that are still marked Valid.
        query.Criteria.AddCondition(ItemParentLookupColumn, ConditionOperator.Equal, ingestionId);
        query.Criteria.AddCondition(ItemValidationStatusColumn, ConditionOperator.Equal, StatusCodes.Valid);

        var result = await _crmService.RetrieveMultipleAsync(query);
        return result.Entities.ToList();
    }

    private async Task UpdateIngestionStatusAsync(Guid id, int status)
    {
        var entity = new Entity("voa_bulkingestion", id);
        entity["statuscode"] = new OptionSetValue(status);
        entity["statecode"] = new OptionSetValue(status is StatusCodes.Completed or StatusCodes.Failed
            ? EntityFields.StateCode.Inactive
            : EntityFields.StateCode.Active);

        await RetryAsync(async () =>
        {
            await _crmService.UpdateAsync(entity);
            return true;
        }, $"UpdateStatus {id}", MaxRetries);
    }

    private async Task TryUpdateIngestionProcessingStateAsync(
        Guid ingestionId,
        int? processingStatusValue,
        DateTime? processingStartedOn,
        DateTime? processedOn,
        string? errorSummary)
    {
        try
        {
            var entity = new Entity("voa_bulkingestion", ingestionId);
            entity["voa_processingstatus"] = processingStatusValue.HasValue
                ? new OptionSetValue(processingStatusValue.Value)
                : null;

            if (processingStartedOn.HasValue)
            {
                entity["voa_processingstartedon"] = processingStartedOn.Value;
            }

            if (processedOn.HasValue)
            {
                entity["voa_processedon"] = processedOn.Value;
            }

            entity["voa_errorsummary"] = errorSummary;

            await RetryAsync(async () =>
            {
                await _crmService.UpdateAsync(entity);
                return true;
            }, $"UpdateProcessingState {ingestionId}", MaxRetries);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update processing state for ingestion {IngestionId}", ingestionId);
        }
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

            await RetryAsync(async () =>
            {
                await _crmService.UpdateAsync(entity);
                return true;
            }, $"MarkItemFailed {itemId}", MaxRetries);
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

            await RetryAsync(async () =>
            {
                await _crmService.UpdateAsync(update);
                return true;
            }, $"UpdateProcessingState {itemId}", MaxRetries);
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

    private static BulkItemCounts CalculateItemCounts(EntityCollection items)
    {
        return new BulkItemCounts
        {
            TotalRows = items.Entities.Count,
            ValidItemCount = items.Entities.Count(e =>
                e.GetAttributeValue<OptionSetValue>(ItemValidationStatusColumn)?.Value == StatusCodes.Valid),
            InvalidItemCount = items.Entities.Count(e =>
                e.GetAttributeValue<OptionSetValue>(ItemValidationStatusColumn)?.Value == StatusCodes.Invalid),
            DuplicateItemCount = items.Entities.Count(e =>
                e.GetAttributeValue<bool?>("voa_isduplicate") ?? false),
            ProcessedItemCount = items.Entities.Count(e =>
                e.GetAttributeValue<OptionSetValue>(ItemValidationStatusColumn)?.Value == StatusCodes.Processed),
            FailedItemCount = items.Entities.Count(e =>
                e.GetAttributeValue<OptionSetValue>(ItemValidationStatusColumn)?.Value == StatusCodes.ItemFailed),
        };
    }

    private sealed record TemplateProcessingSettings(Guid? JobTypeId, bool CreateJob, bool FromTemplate, string? FormatLabel, int? FormatCode);
    private sealed record TimerCreationContext(
        string SubmitUserId,
        string ComponentName,
        string SourceType,
        Guid JobTypeId,
        bool CreateJob,
        string RequestEntityName,
        string JobEntityName);
}


