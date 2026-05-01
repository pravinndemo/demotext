using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System.Diagnostics;
using System.Text.Json;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Routing;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Services;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Activities;

public sealed partial class BulkDataRequestProcessor
{
    private readonly ILogger _logger;
    private readonly IOrganizationServiceAsync2 _dataverseService;
    private readonly SvtProcessingTrackingService _svtTrackingService;

    private const string BulkProcessingStatusColumn = "voa_processingstatus";
    private const string BulkProcessingStartedOnColumn = "voa_processingstartedon";
    private const string BulkProcessedOnColumn = "voa_processedon";
    private const string BulkErrorSummaryColumn = "voa_errorsummary";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public BulkDataRequestProcessor(ILogger logger, IOrganizationServiceAsync2 dataverseService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _svtTrackingService = new SvtProcessingTrackingService(dataverseService, logger);
    }

    public async Task<IActionResult> ProcessRequest(HttpRequest req, BulkRequestAction? bulkAction, bool svtOnly)
    {
        // This method is the HTTP orchestration layer: deserialize, route, validate, then dispatch to the right flow.
        _logger.LogInformation("T_BulkDataHttpTrigger invoked. BulkAction={BulkAction}, SvtOnly={SvtOnly}", bulkAction, svtOnly);

        BulkDataRouteDecisionRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<BulkDataRouteDecisionRequest>(req.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON payload received.");
            return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "INVALID_JSON",
                Message = "Request payload is not valid JSON.",
            });
        }

        if (request is null)
        {
            return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "INVALID_REQUEST",
                Message = "Request payload is required.",
                CorrelationId = request?.CorrelationId,
            });
        }

        _logger.LogInformation(
            "Processing request. BulkAction={BulkAction}, BulkProcessorId={BulkProcessorId}, SourceType={SourceType}, SsuIdsCount={SsuIdsCount}, SsuId={SsuId}, UserId={UserId}, ComponentName={ComponentName}, SvtProcessingId={SvtProcessingId}, RequestedBy={RequestedBy}, CorrelationId={CorrelationId}",
            bulkAction, request.BulkProcessorId, request.SourceType, request.SsuIds?.Count ?? 0, request.SsuId, request.UserId, request.ComponentName, request.SvtProcessingId, request.RequestedBy, request.CorrelationId);

        var decision = BulkDataRouteDecisionBuilder.BuildDecision(request);
        decision.CorrelationId = request.CorrelationId;

        if (!decision.Accepted)
        {
            return new BadRequestObjectResult(decision);
        }

        if (svtOnly && decision.RouteMode != "SVT_TRACKING")
        {
            return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "INVALID_ROUTE_FOR_ENDPOINT",
                Message = "This endpoint only accepts SVT tracking payload (svtProcessingId).",
                CorrelationId = request.CorrelationId,
            });
        }

        if (!svtOnly && bulkAction.HasValue && decision.RouteMode == "SVT_TRACKING")
        {
            return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "INVALID_ROUTE_FOR_ENDPOINT",
                Message = "SVT payload must call /bulk-data/svt-single.",
                CorrelationId = request.CorrelationId,
            });
        }

        if (decision.RouteMode == "SVT_TRACKING")
        {
            return await HandleSvtTrackingAsync(request);
        }

        if (request.BulkProcessorId == Guid.Empty)
        {
            return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "BULK_PROCESSOR_ID_REQUIRED",
                Message = "bulkProcessorId is required for bulk routes.",
                CorrelationId = request.CorrelationId,
            });
        }

        var bulkProcessorEntityName = Environment.GetEnvironmentVariable("BulkProcessorEntityLogicalName") ?? "voa_bulkingestion";
        var statusColumnName = Environment.GetEnvironmentVariable("BulkProcessorStatusColumnName") ?? "statuscode";
        var customStatusColumnName = Environment.GetEnvironmentVariable("BulkProcessorCustomStatusColumnName") ?? statusColumnName;
        var totalRowsColumnName = Environment.GetEnvironmentVariable("BulkProcessorTotalRowsColumnName") ?? "voa_totalrows";
        var validItemCountColumnName = Environment.GetEnvironmentVariable("BulkProcessorValidItemCountColumnName") ?? "voa_validitemcount";
        var draftStatusCode = GetIntFlag("BulkProcessorDraftStatusCode", global::VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants.StatusCodes.Draft);
        var queuedStatusCode = GetIntFlag("BulkProcessorQueuedStatusCode", global::VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants.StatusCodes.Queued);

        var action = bulkAction;
        if (!svtOnly && !action.HasValue)
        {
            return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "INVALID_ACTION",
                Message = "Unsupported bulk endpoint action.",
                BulkProcessorId = request.BulkProcessorId,
                CorrelationId = request.CorrelationId,
                Action = bulkAction?.ToString(),
            });
        }

        Entity bulkProcessor;
        try
        {
            bulkProcessor = _dataverseService.Retrieve(
                bulkProcessorEntityName,
                request.BulkProcessorId,
                new ColumnSet(statusColumnName, customStatusColumnName, totalRowsColumnName, validItemCountColumnName, "voa_processingjobtype", "voa_template"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load bulk processor {BulkProcessorId}.", request.BulkProcessorId);
            return new ObjectResult(new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "BULK_PROCESSOR_LOOKUP_FAILED",
                Message = "Unable to read bulk processor from Dataverse.",
                BulkProcessorId = request.BulkProcessorId,
                CorrelationId = request.CorrelationId,
            })
            {
                StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError,
            };
        }

        // Both SaveItems and SubmitBatch are only allowed while the batch is still Draft.
        var currentStatus = GetFormattedValueOrEmpty(bulkProcessor, customStatusColumnName);
        var currentStatusCode = GetOptionSetValueOrNull(bulkProcessor, customStatusColumnName);
        if (currentStatusCode != draftStatusCode)
        {
            _logger.LogWarning(
                "Batch {BulkProcessorId} is not in Draft status. Current status: {CurrentStatus} ({CurrentStatusCode}). CorrelationId: {CorrelationId}",
                request.BulkProcessorId, currentStatus, currentStatusCode, request.CorrelationId);
            return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "BATCH_NOT_DRAFT",
                Message = $"Batch must be in Draft status for {action}. Current status: {currentStatus} ({currentStatusCode})",
                BulkProcessorId = request.BulkProcessorId,
                CorrelationId = request.CorrelationId,
                Action = action?.ToString(),
            });
        }

        var statusReasonLabel = GetFormattedValueOrEmpty(bulkProcessor, statusColumnName);
        var statusReasonCode = GetOptionSetValueOrNull(bulkProcessor, statusColumnName);
        var totalRows = GetWholeNumberValueOrZero(bulkProcessor, totalRowsColumnName);
        var validItemCount = GetWholeNumberValueOrZero(bulkProcessor, validItemCountColumnName);
        var templateSettings = await ResolveTemplateProcessingSettingsAsync(bulkProcessor);
        var sourceTypeFallback = decision.RouteMode == "BULK_FILE" ? "CSV" : "System Entered";

        decision.BulkProcessorId = request.BulkProcessorId;
        decision.Action = action?.ToString();
        decision.StatusReason = statusReasonLabel;
        decision.StatusReasonCode = statusReasonCode;
        decision.FileType = templateSettings.FormatLabel;
        decision.FileTypeCode = templateSettings.FormatCode;

        decision.SourceType = string.IsNullOrWhiteSpace(request.SourceType)
            ? (templateSettings.FormatLabel ?? sourceTypeFallback)
            : request.SourceType;
        // SaveItems is create/update only. Missing rows are not treated as deletes.
        if (decision.RouteMode == "BULK_SELECTION")
        {
            decision.ReceivedCount = request.SsuIds?.Count ?? 0;
            decision.StagingStatus = "StagingAccepted";
            decision.Message = action == BulkRequestAction.SaveItems
            ? "Selection payload accepted for save/update in Draft. Removals are handled explicitly on the form."
                : "Selection payload accepted for final submit.";
        }
        else
        {
            decision.StagingStatus = "FileStagingAccepted";
            decision.Message = action == BulkRequestAction.SaveItems
            ? $"File payload accepted for save/update in Draft. Function will read {request.FileColumnName ?? "voa_sourcefile"} from Dataverse. Removals are handled explicitly on the form."
                : $"File payload accepted for final submit. Function will read {request.FileColumnName ?? "voa_sourcefile"} from Dataverse.";
        }

        if (action == BulkRequestAction.SubmitBatch)
        {
            try
            {
                // Final submit requires a template-defined format and a usable job type.
                if (!templateSettings.FromTemplate || string.IsNullOrWhiteSpace(templateSettings.FormatLabel))
                {
                    return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
                    {
                        Accepted = false,
                        Code = "TEMPLATE_SOURCE_REQUIRED",
                        Message = "SubmitBatch requires a selected template with Format (voa_format) configured.",
                        BulkProcessorId = request.BulkProcessorId,
                        CorrelationId = request.CorrelationId,
                        Action = action.ToString(),
                    });
                }

                if (totalRows <= 0)
                {
                    return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
                    {
                        Accepted = false,
                        Code = "NO_ITEMS_TO_SUBMIT",
                        Message = "Batch has no items. Save items before submitting.",
                        BulkProcessorId = request.BulkProcessorId,
                        CorrelationId = request.CorrelationId,
                        Action = action.ToString(),
                    });
                }

                if (validItemCount <= 0)
                {
                    return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
                    {
                        Accepted = false,
                        Code = "NO_VALID_ITEMS_TO_SUBMIT",
                        Message = "Batch has no valid items. Fix item validation issues before submitting.",
                        BulkProcessorId = request.BulkProcessorId,
                        CorrelationId = request.CorrelationId,
                        Action = action.ToString(),
                    });
                }

                if (!templateSettings.JobTypeId.HasValue || templateSettings.JobTypeId.Value == Guid.Empty)
                {
                    return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
                    {
                        Accepted = false,
                        Code = "JOB_TYPE_REQUIRED",
                        Message = "Job Type is required. Configure Job Type on the selected template (voa_jobtypelookup) or set Processing Job Type on the bulk ingestion header.",
                        BulkProcessorId = request.BulkProcessorId,
                        CorrelationId = request.CorrelationId,
                        Action = action.ToString(),
                    });
                }

                await TryUpdateProcessingStateAsync(
                    request.BulkProcessorId,
                    bulkProcessorEntityName,
                    processingStatusValue: Constants.StatusCodes.ProcessingStatusProcessing,
                    processingStartedOn: DateTime.UtcNow,
                    processedOn: null,
                    errorSummary: null,
                    correlationId: request.CorrelationId);

                _logger.LogInformation(
                    "SubmitBatch accepted in queue-only mode; timer will create request/job for batch {BulkProcessorId}. CorrelationId: {CorrelationId}",
                    request.BulkProcessorId,
                    request.CorrelationId);
                decision.Message += " Request/job creation deferred to timer (queue-only mode).";

                // The submit action moves the batch from Draft to Queued so the timer can pick it up.
                var updateEntity = new Entity(bulkProcessorEntityName, request.BulkProcessorId);
                updateEntity[customStatusColumnName] = new OptionSetValue(queuedStatusCode);
                _dataverseService.Update(updateEntity);

                _logger.LogInformation(
                    "Batch {BulkProcessorId} status updated to Queued ({QueuedStatusCode}). CorrelationId: {CorrelationId}",
                    request.BulkProcessorId, queuedStatusCode, request.CorrelationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to submit batch {BulkProcessorId}. CorrelationId: {CorrelationId}",
                    request.BulkProcessorId, request.CorrelationId);

                await TryUpdateProcessingStateAsync(
                    request.BulkProcessorId,
                    bulkProcessorEntityName,
                    processingStatusValue: Constants.StatusCodes.ProcessingStatusFailed,
                    processingStartedOn: null,
                    processedOn: DateTime.UtcNow,
                    errorSummary: ex.Message,
                    correlationId: request.CorrelationId);

                return new ObjectResult(new BulkDataRouteDecisionResponse
                {
                    Accepted = false,
                    Code = "SUBMIT_BATCH_FAILED",
                    Message = "Failed to submit batch. See logs for details.",
                    BulkProcessorId = request.BulkProcessorId,
                    CorrelationId = request.CorrelationId,
                    Action = action.ToString(),
                })
                {
                    StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError,
                };
            }

            var bulkIngestionItemEntityName = Environment.GetEnvironmentVariable("BulkIngestionItemEntityLogicalName") ?? "voa_bulkingestionitem";
            var bulkIngestionItemParentLookupColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemParentLookupColumnName") ?? "voa_parentbulkingestion";

            try
            {
                var submitWarning = await GetCrossBatchDuplicateWarningAsync(
                    request.BulkProcessorId,
                    bulkProcessorEntityName,
                    bulkIngestionItemEntityName,
                    bulkIngestionItemParentLookupColumnName,
                    validationMessageColumnName: Environment.GetEnvironmentVariable("BulkIngestionItemValidationMessageColumnName") ?? "voa_validationfailurereason");
                if (!string.IsNullOrWhiteSpace(submitWarning))
                {
                    decision.Message += $" {submitWarning}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to build submit warning for batch {BulkProcessorId}. CorrelationId: {CorrelationId}",
                    request.BulkProcessorId,
                    request.CorrelationId);
                decision.Message += " [Warning: unable to resolve duplicate warning details.]";
            }

            await TryUpdateProcessingStateAsync(
                request.BulkProcessorId,
                bulkProcessorEntityName,
                processingStatusValue: Constants.StatusCodes.ProcessingStatusProcessed,
                processingStartedOn: null,
                processedOn: DateTime.UtcNow,
                errorSummary: null,
                correlationId: request.CorrelationId);
        }
        else if (action == BulkRequestAction.SaveItems)
        {
            // SaveItems: create or update items from payload, then refresh the aggregate counts.
            try
            {
                await TryUpdateProcessingStateAsync(
                    request.BulkProcessorId,
                    bulkProcessorEntityName,
                    processingStatusValue: Constants.StatusCodes.ProcessingStatusProcessing,
                    processingStartedOn: DateTime.UtcNow,
                    processedOn: null,
                    errorSummary: null,
                    correlationId: request.CorrelationId);

                var saveItemsWarning = await HandleSaveItemsAsync(
                    request,
                    request.BulkProcessorId,
                    bulkProcessorEntityName,
                    totalRowsColumnName,
                    validItemCountColumnName);

                // SaveItems is a Draft-only staging operation. It should not advance the parent batch state.
                decision.Message += " Items saved/updated. Batch remains in Draft.";
                if (!string.IsNullOrWhiteSpace(saveItemsWarning))
                {
                    decision.Message += $" {saveItemsWarning}";
                }

                await TryUpdateProcessingStateAsync(
                    request.BulkProcessorId,
                    bulkProcessorEntityName,
                    processingStatusValue: Constants.StatusCodes.ProcessingStatusProcessed,
                    processingStartedOn: null,
                    processedOn: DateTime.UtcNow,
                    errorSummary: null,
                    correlationId: request.CorrelationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to save items for batch {BulkProcessorId}. CorrelationId: {CorrelationId}",
                    request.BulkProcessorId, request.CorrelationId);

                await TryUpdateProcessingStateAsync(
                    request.BulkProcessorId,
                    bulkProcessorEntityName,
                    processingStatusValue: Constants.StatusCodes.ProcessingStatusFailed,
                    processingStartedOn: null,
                    processedOn: DateTime.UtcNow,
                    errorSummary: ex.Message,
                    correlationId: request.CorrelationId);

                return new ObjectResult(new BulkDataRouteDecisionResponse
                {
                    Accepted = false,
                    Code = "SAVE_ITEMS_FAILED",
                    Message = "Failed to save items. See logs for details.",
                    BulkProcessorId = request.BulkProcessorId,
                    CorrelationId = request.CorrelationId,
                    Action = action.ToString(),
                })
                {
                    StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError,
                };
            }
        }
        else
        {
            decision.Message += " Batch remains in Draft.";
        }

        return new AcceptedResult(string.Empty, decision);
    }

    /// <summary>
    /// Saves or updates staged bulk items, validates them, and refreshes the parent counter columns.
    /// </summary>
    private async Task<string?> HandleSaveItemsAsync(
    BulkDataRouteDecisionRequest request,
    Guid bulkProcessorId,
    string bulkProcessorEntityName,
    string totalRowsColumnName,
    string validItemCountColumnName)
{
    EntityCollection bulkIngestionCollection = await _dataverseService.RetrieveMultipleAsync(new FetchExpression(
        FetcherXMLHelper.getBulkIngestionFromID(bulkProcessorId.ToString())));

    Entity bulkIngestion = bulkIngestionCollection.Entities.FirstOrDefault();

    var saveItemsSw = Stopwatch.StartNew();
    var bulkIngestionItemEntityName =
        Environment.GetEnvironmentVariable("BulkIngestionItemEntityLogicalName") ?? "voa_bulkingestionitem";
    var bulkIngestionItemParentLookupColumnName =
        Environment.GetEnvironmentVariable("BulkIngestionItemParentLookupColumnName") ?? "voa_parentbulkingestion";
    var ssuIdColumnName =
        Environment.GetEnvironmentVariable("BulkIngestionItemSSUIdColumnName") ?? "voa_hereditament";
    var sourceValueColumnName =
        Environment.GetEnvironmentVariable("BulkIngestionItemSourceValueColumnName") ?? "voa_source";
        var validationStatusColumnName =
        Environment.GetEnvironmentVariable("BulkIngestionItemValidationStatusColumnName") ?? "voa_validationstatus";

    var assignedManagerColumn =
        Environment.GetEnvironmentVariable("BulkIngestionItemAssignedManager") ?? "voa_assignedmanager";

    var assignedTeamColumn =
        Environment.GetEnvironmentVariable("BulkIngestionItemAssignedTeam") ?? "voa_assignedteam";

    // Chunk size and retry configuration with safe defaults.
    // These settings control how the SaveItems path batches Dataverse writes.
    var executeMultipleChunkSize = GetIntConfigValue("BulkSaveItemsExecuteMultipleChunkSize", defaultValue: 100, min: 1, max: 1000);
    var itemUpsertChunkSize = GetIntConfigValue("BulkSaveItemsItemUpsertChunkSize", defaultValue: 100, min: 1, max: 1000);
    var chunkMaxRetries = GetIntConfigValue("BulkSaveItemsMaxRetries", defaultValue: 3, min: 0, max: 10);
    var chunkBaseDelayMs = GetIntConfigValue("BulkSaveItemsBaseDelayMs", defaultValue: 500, min: 50, max: 30000);

    var pendingStatusValue = new OptionSetValue(Constants.StatusCodes.Pending);

    var assignmentMode = GetOptionSetValueOrNull(bulkIngestion, "voa_assignmentmode");
    EntityReference assignmentValue = new EntityReference();

    if (assignmentMode != null)
    {
        if (assignmentMode == Constants.StatusCodes.Team)
        {
            assignmentValue = new EntityReference(
                "team",
                (Guid)(bulkIngestion.GetAttributeValue<EntityReference>("voa_assignedteam")?.Id));
        }
        else
        {
            assignmentValue = new EntityReference(
                "systemuser",
                (Guid)(bulkIngestion.GetAttributeValue<EntityReference>("voa_assignedmanager")?.Id));
        }
    }

    _logger.LogInformation(
        "Starting SaveItems for batch {BulkProcessorId}, route mode: {RouteMode}, SSU count: {SsuCount}, CorrelationId: {CorrelationId}",
        bulkProcessorId,
        request.SsuIds?.Count > 0 ? "BULK_SELECTION" : "BULK_FILE",
        request.SsuIds?.Count ?? 0,
        request.CorrelationId);

    var bulkItemWriter = new DataverseBulkItemWriter(_dataverseService);
    var bulkDataIngestionItemRequests = new List<OrganizationRequest>();

    // Build flat list of voa_UpsertHereditamentLinkV1 requests; executed in chunks below.
    var executeMultipleRequests = new List<OrganizationRequest>();

    var csvRowsParsed = 0;

    // BULK_SELECTION creates or updates items directly from the selected SSU ids.
    if (request.SsuIds?.Count > 0)
    {
        // Retrieve existing items for this batch
        var existingLookupSw = Stopwatch.StartNew();

        var query = new QueryExpression(bulkIngestionItemEntityName)
        {
            ColumnSet = new ColumnSet(true),
            Criteria = new FilterExpression()
            {
                Conditions =
                {
                    new ConditionExpression(
                        bulkIngestionItemParentLookupColumnName,
                        ConditionOperator.Equal,
                        bulkProcessorId),

                    new ConditionExpression(
                        ssuIdColumnName,
                        ConditionOperator.In,
                        request.SsuIds.Select(x => x.StatutorySpatialUnitId).ToArray())
                }
            }
        };

        var existingItems = await _dataverseService.RetrieveMultipleAsync(query);

        existingLookupSw.Stop();

        var existingBySSUId = existingItems.Entities.ToDictionary(
            e => e.GetAttributeValue<string>(ssuIdColumnName) ?? string.Empty,
            e => e);

        _logger.LogInformation(
            "Found {ExistingItemCount} existing items for batch {BulkProcessorId}, need to process {RequestedCount} SSU IDs. CorrelationId: {CorrelationId}",
            existingItems.Entities.Count,
            bulkProcessorId,
            request.SsuIds.Count,
            request.CorrelationId);

        _logger.LogInformation(
            "Performance.SaveItemsExistingLookup Batch={BulkProcessorId} ExistingItemCount={ExistingItemCount} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
            bulkProcessorId,
            existingItems.Entities.Count,
            existingLookupSw.ElapsedMilliseconds,
            request.CorrelationId);

        // Build upsert requests: update existing, create new
        foreach (var ssuId in request.SsuIds)
        {
            Entity itemEntity;

            executeMultipleRequests.Add(UpsertRequest(ssuId.StatutorySpatialUnitId));

            if (existingBySSUId.TryGetValue(ssuId.StatutorySpatialUnitId.ToString(), out var existingItem))
            {
                // Update existing
                itemEntity = new Entity(bulkIngestionItemEntityName, existingItem.Id)
                {
                    [ssuIdColumnName] =
                        new EntityReference("voa_ssu", ssuId.StatutorySpatialUnitId),

                    [validationStatusColumnName] = pendingStatusValue
                };
            }
            else
            {
                // Create new
                itemEntity = new Entity(bulkIngestionItemEntityName)
                {
                    [bulkIngestionItemParentLookupColumnName] =
                        new EntityReference(bulkProcessorEntityName, bulkProcessorId),

                    [ssuIdColumnName] =
                        new EntityReference("voa_ssu", ssuId.StatutorySpatialUnitId),

                    [validationStatusColumnName] = pendingStatusValue
                };
            }

            bulkDataIngestionItemRequests.Add(
                DataverseBulkItemWriter.BuildUpsertRequest(itemEntity));
        }
    }

        // BULK_FILE reads the CSV from Dataverse and converts each row into a staged item.
        else
        {
        var fileColumnName = request.FileColumnName ?? "voa_sourcefile";
        var csvParser = new CsvFileParser(_dataverseService, _logger);

        try
        {
            var parseCsvSw = Stopwatch.StartNew();

            var csvRows = await csvParser.RetriveSsuIdFromFile(
                bulkProcessorId,
                bulkProcessorEntityName,
                fileColumnName);

            parseCsvSw.Stop();
            csvRowsParsed = csvRows.Count;

            _logger.LogInformation(
                "Parsed {RowCount} rows from CSV file for batch {BulkProcessorId}. CorrelationId: {CorrelationId}",
                csvRows.Count,
                bulkProcessorId,
                request.CorrelationId);

            _logger.LogInformation(
                "Performance.SaveItemsCsvParse Batch={BulkProcessorId} ParsedRows={ParsedRows} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
                bulkProcessorId,
                csvRows.Count,
                parseCsvSw.ElapsedMilliseconds,
                request.CorrelationId);

            // Build upsert requests from CSV rows
            foreach (var csvRow in csvRows)
            {
                executeMultipleRequests.Add(UpsertRequest(new Guid(csvRow.SsuId)));

                var itemEntity = new Entity(bulkIngestionItemEntityName)
                {
                    [bulkIngestionItemParentLookupColumnName] =
                        new EntityReference(bulkProcessorEntityName, bulkProcessorId),

                    [ssuIdColumnName] =
                        new EntityReference("voa_ssu", new Guid(csvRow.SsuId)),

                    [sourceValueColumnName] =
                        new OptionSetValue(358800000),

                    //["voa_sourcerownumber"] = csvRow.SourceRowNumber,

                    [assignedManagerColumn] =
                        assignmentMode == Constants.StatusCodes.Manager ? assignmentValue : null,

                    [assignedTeamColumn] =
                        assignmentMode == Constants.StatusCodes.Team ? assignmentValue : null,

                    [validationStatusColumnName] = pendingStatusValue
                };

                bulkDataIngestionItemRequests.Add(
                    DataverseBulkItemWriter.BuildUpsertRequest(itemEntity));
            }

            _logger.LogInformation(
                "Built {RequestCount} upsert requests from CSV for batch {BulkProcessorId}. CorrelationId: {CorrelationId}",
                bulkDataIngestionItemRequests.Count,
                bulkProcessorId,
                request.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to parse CSV from file column {FileColumnName} for batch {BulkProcessorId}. CorrelationId: {CorrelationId}",
                fileColumnName,
                bulkProcessorId,
                request.CorrelationId);

            throw;
        }
    }

    // Execute the staged writes in chunks; partial failures are allowed if at least one request succeeds.
    string? writeChunkWarning = null;
    if (executeMultipleRequests.Count > 0 || bulkDataIngestionItemRequests.Count > 0)
    {
        var writeSw = Stopwatch.StartNew();
        var allChunkErrors = new List<string>();
        var totalSucceededRequests = 0;
        var totalFailedRequests = 0;

            // 1) Create/update the request-facing records first.
            if (executeMultipleRequests.Count > 0)
            {
            _logger.LogInformation(
                "Executing ExecuteMultiple in chunks. TotalRequests={TotalRequests} ChunkSize={ChunkSize} CorrelationId={CorrelationId}",
                executeMultipleRequests.Count, executeMultipleChunkSize, request.CorrelationId);

            var (succeeded, failed, errors) = await ExecuteMultipleInChunksAsync(
                executeMultipleRequests,
                executeMultipleChunkSize,
                chunkMaxRetries,
                chunkBaseDelayMs,
                "SaveItems.ExecuteMultiple",
                request.CorrelationId);

            totalSucceededRequests += succeeded;
            totalFailedRequests += failed;
            allChunkErrors.AddRange(errors);
        }

        // 2) Bulk ingestion item upserts in chunks
        if (bulkDataIngestionItemRequests.Count > 0)
        {
            _logger.LogInformation(
                "Executing ingestion item upserts in chunks. TotalRequests={TotalRequests} ChunkSize={ChunkSize} CorrelationId={CorrelationId}",
                bulkDataIngestionItemRequests.Count, itemUpsertChunkSize, request.CorrelationId);

            var (succeeded, failed, errors) = await ExecuteMultipleInChunksAsync(
                bulkDataIngestionItemRequests,
                itemUpsertChunkSize,
                chunkMaxRetries,
                chunkBaseDelayMs,
                "SaveItems.ItemUpserts",
                request.CorrelationId);

            totalSucceededRequests += succeeded;
            totalFailedRequests += failed;
            allChunkErrors.AddRange(errors);
        }

        writeSw.Stop();

        _logger.LogInformation(
            "Performance.SaveItemsWrite Batch={BulkProcessorId} ExecuteMultipleRequests={ExecuteMultipleRequests} ItemUpsertRequests={ItemUpsertRequests} SucceededRequests={SucceededRequests} FailedRequests={FailedRequests} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
            bulkProcessorId,
            executeMultipleRequests.Count,
            bulkDataIngestionItemRequests.Count,
            totalSucceededRequests,
            totalFailedRequests,
            writeSw.ElapsedMilliseconds,
            request.CorrelationId);

        if (allChunkErrors.Count > 0)
        {
            if (totalSucceededRequests == 0)
            {
                // All writes failed — escalate so the caller returns an error response.
                throw new InvalidOperationException(
                    $"All write requests failed for batch {bulkProcessorId}. Errors: {string.Join("; ", allChunkErrors)}");
            }

            // Some writes succeeded — record as warning and continue (partial success).
            writeChunkWarning = $"[PartialWriteFailure: {totalFailedRequests} of {totalSucceededRequests + totalFailedRequests} request(s) failed. First error: {allChunkErrors[0]}]";
            _logger.LogWarning(
                "Partial write failure during SaveItems. FailedRequests={FailedRequests} TotalRequests={TotalRequests} CorrelationId={CorrelationId}",
                totalFailedRequests, totalSucceededRequests + totalFailedRequests, request.CorrelationId);
        }
    }

        // Validate after the writes so the counters reflect the persisted state, not just the payload.
        var validateSw = Stopwatch.StartNew();

    var validator = new BulkItemValidator(_dataverseService, _logger);

    var validationResult = await validator.ValidateBatchItemsAsync(
        bulkProcessorId,
        bulkIngestionItemEntityName,
        bulkIngestionItemParentLookupColumnName);

    validateSw.Stop();

    _logger.LogInformation(
        "Validation result for batch {BulkProcessorId}: Valid={ValidCount}, Invalid={InvalidCount}, Duplicate={DuplicateCount}. CorrelationId: {CorrelationId}",
        bulkProcessorId,
        validationResult.ValidCount,
        validationResult.InvalidCount,
        validationResult.DuplicateCount,
        request.CorrelationId);

    _logger.LogInformation(
        "Performance.SaveItemsValidation Batch={BulkProcessorId} Total={Total} Valid={Valid} Invalid={Invalid} Duplicate={Duplicate} Updated={Updated} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
        bulkProcessorId,
        validationResult.TotalCount,
        validationResult.ValidCount,
        validationResult.InvalidCount,
        validationResult.DuplicateCount,
        validationResult.UpdatedCount,
        validateSw.ElapsedMilliseconds,
        request.CorrelationId);

    var recalcSw = Stopwatch.StartNew();

    var allItemsQuery = new QueryExpression(bulkIngestionItemEntityName)
    {
        ColumnSet = new ColumnSet(validationStatusColumnName, "voa_isduplicate"),
        Criteria = new FilterExpression()
        {
            Conditions =
            {
                new ConditionExpression(
                    bulkIngestionItemParentLookupColumnName,
                    ConditionOperator.Equal,
                    bulkProcessorId),
            }
        }
    };

    var allItems = await _dataverseService.RetrieveMultipleAsync(allItemsQuery);
    var recalculatedCounts = CalculateItemCounts(allItems);

    recalcSw.Stop();

    _logger.LogInformation(
        "Recalculated counters for batch {BulkProcessorId}: Total={TotalRows}, Valid={ValidCount}, Invalid={InvalidCount}, Duplicate={DuplicateCount}. CorrelationId: {CorrelationId}",
        bulkProcessorId,
        recalculatedCounts.TotalRows,
        recalculatedCounts.ValidItemCount,
        recalculatedCounts.InvalidItemCount,
        recalculatedCounts.DuplicateItemCount,
        request.CorrelationId);

    // Refresh the parent bulk header counters from the newly validated item set.
    var updateCountersSw = Stopwatch.StartNew();

    bulkItemWriter.UpdateBatchCounters(bulkProcessorId, recalculatedCounts);

    updateCountersSw.Stop();

    saveItemsSw.Stop();

    _logger.LogInformation(
        "SaveItems completed for batch {BulkProcessorId}. CorrelationId: {CorrelationId}",
        bulkProcessorId,
        request.CorrelationId);

    _logger.LogInformation(
        "Performance.SaveItemsSummary Batch={BulkProcessorId} RouteMode={RouteMode} SelectionInputCount={SelectionInputCount} CsvRowsParsed={CsvRowsParsed} ExecuteMultipleRequests={ExecuteMultipleRequests} ItemUpsertRequests={ItemUpsertRequests} TotalRows={TotalRows} ValidItems={ValidItems} InvalidItems={InvalidItems} DuplicateItems={DuplicateItems} RecalcElapsedMs={RecalcElapsedMs} CounterUpdateElapsedMs={CounterUpdateElapsedMs} TotalElapsedMs={TotalElapsedMs} CorrelationId={CorrelationId}",
        bulkProcessorId,
        request.SsuIds?.Count > 0 ? "BULK_SELECTION" : "BULK_FILE",
        request.SsuIds?.Count ?? 0,
        csvRowsParsed,
        executeMultipleRequests.Count,
        bulkDataIngestionItemRequests.Count,
        recalculatedCounts.TotalRows,
        recalculatedCounts.ValidItemCount,
        recalculatedCounts.InvalidItemCount,
        recalculatedCounts.DuplicateItemCount,
        recalcSw.ElapsedMilliseconds,
        updateCountersSw.ElapsedMilliseconds,
        saveItemsSw.ElapsedMilliseconds,
        request.CorrelationId);

    var warnings = new List<string>();
    if (validationResult.CrossBatchDuplicateBatches.Count > 0)
        warnings.Add(CrossBatchDuplicateMessageHelper.BuildErrorMessage(validationResult.CrossBatchDuplicateBatches));
    if (writeChunkWarning is not null)
        warnings.Add(writeChunkWarning);

    return warnings.Count > 0 ? string.Join(" ", warnings) : null;
}

    private async Task<string?> GetCrossBatchDuplicateWarningAsync(
        Guid bulkProcessorId,
        string bulkProcessorEntityName,
        string bulkIngestionItemEntityName,
        string bulkIngestionItemParentLookupColumnName,
        string validationMessageColumnName)
    {
        try
        {
            var query = new QueryExpression(bulkIngestionItemEntityName)
            {
                ColumnSet = new ColumnSet(validationMessageColumnName),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(bulkIngestionItemParentLookupColumnName, ConditionOperator.Equal, bulkProcessorId),
                        new ConditionExpression(validationMessageColumnName, ConditionOperator.Like, "ERR_DUP_SSU_OTHER_BATCH%"),
                    }
                }
            };

            var result = await _dataverseService.RetrieveMultipleAsync(query);
            var duplicateMessages = result.Entities
                .Select(entity => entity.GetAttributeValue<string>(validationMessageColumnName)?.Trim())
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (duplicateMessages.Count == 0)
            {
                return null;
            }

            return $"Cross-batch duplicate warning: {string.Join(" ", duplicateMessages)}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Unable to build submit warning for batch {BulkProcessorId} using entity {BulkProcessorEntityName}.",
                bulkProcessorId,
                bulkProcessorEntityName);
            return null;
        }
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
            var template = await _dataverseService.RetrieveAsync(
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

    private static string? ResolveSubmitUserId(BulkDataRouteDecisionRequest request)
    {
        if (Guid.TryParse(request.UserId, out var directUserId))
        {
            return directUserId.ToString();
        }

        if (Guid.TryParse(request.RequestedBy, out var requestedByUserId))
        {
            return requestedByUserId.ToString();
        }

        return null;
    }

    private static bool GetBooleanFlag(string key, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    private static int GetIntFlag(string key, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        return int.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    private BulkItemCounts CalculateItemCounts(EntityCollection items)
    {
        var validationStatusColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemValidationStatusColumnName") ?? "voa_validationstatus";
        var isDuplicateColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemIsDuplicateColumnName") ?? "voa_isduplicate";

        var counts = new BulkItemCounts
        {
            TotalRows = items.Entities.Count,
            ValidItemCount = items.Entities.Count(e =>
            {
                var status = e.GetAttributeValue<OptionSetValue>(validationStatusColumnName)?.Value.ToString() ?? string.Empty;
                return status.Equals((Constants.StatusCodes.Valid).ToString(), StringComparison.OrdinalIgnoreCase);
            }),
            InvalidItemCount = items.Entities.Count(e =>
            {
                var status = e.GetAttributeValue<OptionSetValue>(validationStatusColumnName)?.Value.ToString() ?? string.Empty;
                return status.Equals((Constants.StatusCodes.Invalid).ToString(), StringComparison.OrdinalIgnoreCase);
            }),
            DuplicateItemCount = items.Entities.Count(e =>
            {
                var isDuplicate = e.GetAttributeValue<bool?>(isDuplicateColumnName) ?? false;
                return isDuplicate;
            }),
            ProcessedItemCount = items.Entities.Count(e =>
            {
                var status = e.GetAttributeValue<OptionSetValue>(validationStatusColumnName)?.Value.ToString() ?? string.Empty;
                return status.Equals((Constants.StatusCodes.Processed).ToString(), StringComparison.OrdinalIgnoreCase);
            }),
            FailedItemCount = items.Entities.Count(e =>
            {
                var status = e.GetAttributeValue<OptionSetValue>(validationStatusColumnName)?.Value.ToString() ?? string.Empty;
                return status.Equals((Constants.StatusCodes.ItemFailed).ToString(), StringComparison.OrdinalIgnoreCase);
            }),
        };

        return counts;
    }

    private static string GetFormattedValueOrEmpty(Entity entity, string columnName)
    {
        return entity.FormattedValues.TryGetValue(columnName, out var formatted) ? formatted : string.Empty;
    }

    private static int? GetOptionSetValueOrNull(Entity entity, string columnName)
    {
        return entity.GetAttributeValue<OptionSetValue>(columnName)?.Value;
    }

    private static int GetWholeNumberValueOrZero(Entity entity, string columnName)
    {
        return entity.GetAttributeValue<int?>(columnName) ?? 0;
    }

    private async Task TryUpdateProcessingStateAsync(
        Guid bulkProcessorId,
        string bulkProcessorEntityName,
        int? processingStatusValue,
        DateTime? processingStartedOn,
        DateTime? processedOn,
        string? errorSummary,
        string? correlationId)
    {
        try
        {
            var update = new Entity(bulkProcessorEntityName, bulkProcessorId)
            {
                [BulkProcessingStatusColumn] = processingStatusValue.HasValue
                    ? new OptionSetValue(processingStatusValue.Value)
                    : null,
                [BulkProcessingStartedOnColumn] = processingStartedOn,
                [BulkProcessedOnColumn] = processedOn,
                [BulkErrorSummaryColumn] = errorSummary,
            };

            _dataverseService.Update(update);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not update processing state for batch {BulkProcessorId}. CorrelationId: {CorrelationId}",
                bulkProcessorId,
                correlationId);
        }
    }

    private sealed record TemplateProcessingSettings(Guid? JobTypeId, bool CreateJob, bool FromTemplate, string? FormatLabel, int? FormatCode);

    /// <summary>
    /// Reads an integer environment variable and clamps it to [min, max].
    /// Logs a warning and returns <paramref name="defaultValue"/> when the value is absent, unparseable, or out of range.
    /// </summary>
    private int GetIntConfigValue(string key, int defaultValue, int min = 1, int max = 1000)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (raw is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, out var parsed))
        {
            _logger.LogWarning(
                "Configuration key {Key} has an invalid integer value '{Value}'. Using default {Default}.",
                key, raw, defaultValue);
            return defaultValue;
        }

        if (parsed < min || parsed > max)
        {
            _logger.LogWarning(
                "Configuration key {Key} value {Parsed} is out of range [{Min}, {Max}]. Using default {Default}.",
                key, parsed, min, max, defaultValue);
            return defaultValue;
        }

        return parsed;
    }

    /// <summary>
    /// Retries <paramref name="operation"/> up to <paramref name="maxRetries"/> times using
    /// exponential back-off starting at <paramref name="baseDelayMs"/> ms.
    /// </summary>
    private async Task<T> RetryWithBackoffAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        int maxRetries,
        int baseDelayMs,
        string? correlationId)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                attempt++;
                var delay = baseDelayMs * (int)Math.Pow(2, attempt - 1);
                _logger.LogWarning(
                    ex,
                    "Retry {OperationName} attempt {Attempt}/{MaxRetries}. DelayMs={Delay}. CorrelationId={CorrelationId}",
                    operationName, attempt, maxRetries, delay, correlationId);
                await Task.Delay(delay);
            }
        }
    }

    /// <summary>
    /// Executes a list of <see cref="OrganizationRequest"/>s as multiple smaller
    /// <see cref="ExecuteMultipleRequest"/> calls (chunks), each retried with exponential back-off.
    /// Uses <c>ContinueOnError=true</c> per chunk so individual item faults don't abort the chunk.
    /// </summary>
    /// <returns>
    /// A tuple of succeeded request count, failed request count, and error messages for any
    /// chunk that exhausted retries or returned item-level faults.
    /// </returns>
    private async Task<(int SucceededRequests, int FailedRequests, List<string> Errors)> ExecuteMultipleInChunksAsync(
        List<OrganizationRequest> requests,
        int chunkSize,
        int maxRetries,
        int baseDelayMs,
        string operationName,
        string? correlationId)
    {
        var succeededRequests = 0;
        var failedRequests = 0;
        var errors = new List<string>();

        if (requests.Count == 0)
        {
            return (succeededRequests, failedRequests, errors);
        }

        var total = requests.Count;
        var totalChunks = (int)Math.Ceiling(total / (double)chunkSize);

        for (var chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
        {
            var start = chunkIndex * chunkSize;
            var count = Math.Min(chunkSize, total - start);

            var chunkRequest = new ExecuteMultipleRequest
            {
                Settings = new ExecuteMultipleSettings
                {
                    ContinueOnError = true,
                    ReturnResponses = true,
                },
                Requests = new OrganizationRequestCollection(),
            };

            for (var i = 0; i < count; i++)
            {
                chunkRequest.Requests.Add(requests[start + i]);
            }

            var chunkSw = Stopwatch.StartNew();
            try
            {
                var response = await RetryWithBackoffAsync(
                    async () =>
                    {
                        return (ExecuteMultipleResponse)await _dataverseService.ExecuteAsync(chunkRequest);
                    },
                    $"{operationName} chunk {chunkIndex + 1}/{totalChunks}",
                    maxRetries,
                    baseDelayMs,
                    correlationId);

                chunkSw.Stop();
                var chunkFaults = response.Responses
                    .Where(itemResponse => itemResponse.Fault is not null)
                    .ToList();

                if (chunkFaults.Count == 0)
                {
                    succeededRequests += count;
                    _logger.LogInformation(
                        "ExecuteMultiple chunk succeeded. Operation={Operation} Chunk={Chunk}/{Total} Count={Count} ElapsedMs={Elapsed} CorrelationId={CorrelationId}",
                        operationName, chunkIndex + 1, totalChunks, count, chunkSw.ElapsedMilliseconds, correlationId);
                    continue;
                }

                succeededRequests += count - chunkFaults.Count;
                failedRequests += chunkFaults.Count;

                foreach (var itemResponse in chunkFaults)
                {
                    errors.Add(
                        $"Chunk {chunkIndex + 1}/{totalChunks} request {itemResponse.RequestIndex} failed: {itemResponse.Fault!.Message}");
                }

                _logger.LogInformation(
                    "ExecuteMultiple chunk completed with faults. Operation={Operation} Chunk={Chunk}/{Total} Count={Count} Faults={Faults} ElapsedMs={Elapsed} CorrelationId={CorrelationId}",
                    operationName, chunkIndex + 1, totalChunks, count, chunkFaults.Count, chunkSw.ElapsedMilliseconds, correlationId);
            }
            catch (Exception ex)
            {
                chunkSw.Stop();
                failedRequests += count;
                var errMsg = $"Chunk {chunkIndex + 1}/{totalChunks} failed after {maxRetries} retries: {ex.Message}";
                errors.Add(errMsg);

                _logger.LogError(
                    ex,
                    "ExecuteMultiple chunk failed. Operation={Operation} Chunk={Chunk}/{Total} Count={Count} ElapsedMs={Elapsed} CorrelationId={CorrelationId}",
                    operationName, chunkIndex + 1, totalChunks, count, chunkSw.ElapsedMilliseconds, correlationId);
            }
        }

        return (succeededRequests, failedRequests, errors);
    }

    private static OrganizationRequest UpsertRequest(Guid guid)
    {
        ParameterCollection parameters = new ParameterCollection
        {
            { "HereditamentId", guid }
        };

        OrganizationRequest upsertHereditamentRequest = new OrganizationRequest()
        {
            RequestName = "voa_UpsertHereditamentLinkV1",
            Parameters = parameters
        };

        return upsertHereditamentRequest;
    }
}


