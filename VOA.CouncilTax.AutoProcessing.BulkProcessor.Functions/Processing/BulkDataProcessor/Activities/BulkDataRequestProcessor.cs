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

public sealed class BulkDataRequestProcessor
{
    private readonly ILogger _logger;
    private readonly IOrganizationServiceAsync2 _dataverseService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public BulkDataRequestProcessor(ILogger logger, IOrganizationServiceAsync2 dataverseService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
    }

    public async Task<IActionResult> ProcessRequest(HttpRequest req, BulkRequestAction? bulkAction, bool svtOnly)
    {
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
            "Processing request. BulkAction={BulkAction}, BulkProcessorId={BulkProcessorId}, SourceType={SourceType}, SsuIdsCount={SsuIdsCount}, SsuId={SsuId}, UserId={UserId}, ComponentName={ComponentName}, RequestedBy={RequestedBy}, CorrelationId={CorrelationId}",
            bulkAction, request.BulkProcessorId, request.SourceType, request.SsuIds?.Count ?? 0, request.SsuId, request.UserId, request.ComponentName, request.RequestedBy, request.CorrelationId);

        var decision = BulkDataRouteDecisionBuilder.BuildDecision(request);
        decision.CorrelationId = request.CorrelationId;

        if (!decision.Accepted)
        {
            return new BadRequestObjectResult(decision);
        }

        if (svtOnly && decision.RouteMode != "SVT_SINGLE")
        {
            return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "INVALID_ROUTE_FOR_ENDPOINT",
                Message = "This endpoint only accepts SVT payload (ssuid + userId + componentName).",
                CorrelationId = request.CorrelationId,
            });
        }

        if (!svtOnly && bulkAction.HasValue && decision.RouteMode == "SVT_SINGLE")
        {
            return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "INVALID_ROUTE_FOR_ENDPOINT",
                Message = "SVT payload must call /bulk-data/svt-single.",
                CorrelationId = request.CorrelationId,
            });
        }

        if (decision.RouteMode == "SVT_SINGLE")
        {
            // Handle SVT direct request/job creation
            try
            {
                _logger.LogInformation(
                    "Processing SVT single-item request. SsuId={SsuId}, UserId={UserId}, ComponentName={ComponentName}, CorrelationId={CorrelationId}",
                    request.SsuId, request.UserId, request.ComponentName, request.CorrelationId);

                var creator = new RequestJobCreationService(_dataverseService, _logger);
                var creationResult = await creator.CreateSingleAsync(
                    new RequestJobCreateItem
                    {
                        SsuId = request.SsuId ?? string.Empty,
                        SourceType = decision.SourceType ?? "SVT",
                        SourceValue = request.SsuId ?? string.Empty,
                    },
                    request.UserId ?? string.Empty,
                    request.ComponentName ?? "DirectAPI",
                    decision.SourceType);

                if (!creationResult.Success)
                {
                    _logger.LogError(
                        "SVT creation failed. ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}, CorrelationId={CorrelationId}",
                        creationResult.ErrorCode, creationResult.ErrorMessage, request.CorrelationId);

                    return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
                    {
                        Accepted = false,
                        Code = creationResult.ErrorCode,
                        Message = $"SVT request/job creation failed: {creationResult.ErrorMessage}",
                        CorrelationId = request.CorrelationId,
                        Action = "SvtSingle",
                    });
                }

                decision.Action = "SvtSingle";
                decision.SourceType = creationResult.SourceType;
                decision.StagingStatus = "Created";
                decision.ReceivedCount = 1;
                decision.RequestId = creationResult.RequestId;
                decision.JobId = creationResult.JobId == Guid.Empty ? null : creationResult.JobId;
                decision.Message = $"SVT request/job created successfully. RequestId={creationResult.RequestId:N}, JobId={creationResult.JobId:N}";

                _logger.LogInformation(
                    "SVT creation succeeded. RequestId={RequestId}, JobId={JobId}, CorrelationId={CorrelationId}",
                    creationResult.RequestId, creationResult.JobId, request.CorrelationId);

                return new AcceptedResult(string.Empty, decision);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error processing SVT request. SsuId={SsuId}, CorrelationId={CorrelationId}",
                    request.SsuId, request.CorrelationId);

                return new ObjectResult(new BulkDataRouteDecisionResponse
                {
                    Accepted = false,
                    Code = "SVT_CREATION_ERROR",
                    Message = "SVT request/job creation failed. See logs for details.",
                    CorrelationId = request.CorrelationId,
                    Action = "SvtSingle",
                })
                {
                    StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError,
                };  
            }
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

        // Gate check: both SaveItems and SubmitBatch run only when batch is Draft.
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
        // SaveItems is create/update only. Deletions are explicit user actions on the form/subgrid
        // and are not inferred from missing items in the incoming payload.
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

            _logger.LogInformation(
                "SubmitBatch accepted in queue-only mode; timer will create request/job for batch {BulkProcessorId}. CorrelationId: {CorrelationId}",
                request.BulkProcessorId,
                request.CorrelationId);
            decision.Message += " Request/job creation deferred to timer (queue-only mode).";

            // SubmitBatch is the only action that transitions batch status Draft -> Queued.
            try
            {
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
                    "Failed to update batch {BulkProcessorId} status to Queued ({QueuedStatusCode}). CorrelationId: {CorrelationId}",
                    request.BulkProcessorId, queuedStatusCode, request.CorrelationId);
                // Continue processing even if status update fails, as staging may have partially succeeded
                decision.Message += " [Warning: Status transition failed]";
            }

            var bulkIngestionItemEntityName = Environment.GetEnvironmentVariable("BulkIngestionItemEntityLogicalName") ?? "voa_bulkingestionitem";
            var bulkIngestionItemParentLookupColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemParentLookupColumnName") ?? "voa_parentbulkingestion";

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
        else if (action == BulkRequestAction.SaveItems)
        {
            // SaveItems: create or update items from payload, recalculate counters
            try
            {
                var saveItemsWarning = await HandleSaveItemsAsync(
                    request,
                    request.BulkProcessorId,
                    bulkProcessorEntityName,
                    totalRowsColumnName,
                    validItemCountColumnName);

                decision.Message += " Items saved/updated. Batch remains in Draft.";
                if (!string.IsNullOrWhiteSpace(saveItemsWarning))
                {
                    decision.Message += $" {saveItemsWarning}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to save items for batch {BulkProcessorId}. CorrelationId: {CorrelationId}",
                    request.BulkProcessorId, request.CorrelationId);
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
        Environment.GetEnvironmentVariable("BulkInestionItemValidationStatusColumnName") ?? "voa_validationstatus";

    var assignedManagerColumn =
        Environment.GetEnvironmentVariable("BulkIngestionItemAssignedManager") ?? "voa_assignedmanager";

    var assignedTeamColumn =
        Environment.GetEnvironmentVariable("BulkIngestionItemAssignedTeam") ?? "voa_assignedteam";

    // Chunk size and retry configuration with safe defaults.
    // See README.md for documentation on these environment variables.
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

    // For BULK_SELECTION: create/update items for provided SSU IDs
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

    // For BULK_FILE: parse CSV from Dataverse file column
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

    // Execute batch writes in chunks with retry and graceful partial-success handling.
    string? writeChunkWarning = null;
    if (executeMultipleRequests.Count > 0 || bulkDataIngestionItemRequests.Count > 0)
    {
        var writeSw = Stopwatch.StartNew();
        var allChunkErrors = new List<string>();
        var totalSucceededChunks = 0;
        var totalFailedChunks = 0;

        // 1) voa_UpsertHereditamentLinkV1 requests in chunks
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

            totalSucceededChunks += succeeded;
            totalFailedChunks += failed;
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

            totalSucceededChunks += succeeded;
            totalFailedChunks += failed;
            allChunkErrors.AddRange(errors);
        }

        writeSw.Stop();

        _logger.LogInformation(
            "Performance.SaveItemsWrite Batch={BulkProcessorId} ExecuteMultipleRequests={ExecuteMultipleRequests} ItemUpsertRequests={ItemUpsertRequests} SucceededChunks={SucceededChunks} FailedChunks={FailedChunks} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
            bulkProcessorId,
            executeMultipleRequests.Count,
            bulkDataIngestionItemRequests.Count,
            totalSucceededChunks,
            totalFailedChunks,
            writeSw.ElapsedMilliseconds,
            request.CorrelationId);

        if (allChunkErrors.Count > 0)
        {
            if (totalSucceededChunks == 0)
            {
                // All chunks failed — escalate so the caller returns an error response.
                throw new InvalidOperationException(
                    $"All write chunks failed for batch {bulkProcessorId}. Errors: {string.Join("; ", allChunkErrors)}");
            }

            // Some chunks succeeded — record as warning and continue (partial success).
            writeChunkWarning = $"[PartialWriteFailure: {totalFailedChunks} of {totalSucceededChunks + totalFailedChunks} chunk(s) failed. First error: {allChunkErrors[0]}]";
            _logger.LogWarning(
                "Partial chunk failure during SaveItems. FailedChunks={FailedChunks} TotalChunks={TotalChunks} CorrelationId={CorrelationId}",
                totalFailedChunks, totalSucceededChunks + totalFailedChunks, request.CorrelationId);
        }
    }

    // Validate items after writes (staging validation)
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

    // Update batch counters
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

    private async Task<RequestJobBatchResult> CreateRequestsAndJobsForValidItemsAsync(
        Guid bulkProcessorId,
        string submitUserId,
        string componentName,
        string sourceType,
        string? correlationId,
        Guid? jobTypeId = null,
        bool createJob = true,
        string? processingRunId = null)
    {
        var createFlowSw = Stopwatch.StartNew();
        var bulkProcessorEntityName = Environment.GetEnvironmentVariable("BulkProcessorEntityLogicalName") ?? "voa_bulkingestion";
        var bulkIngestionItemEntityName = Environment.GetEnvironmentVariable("BulkIngestionItemEntityLogicalName") ?? "voa_bulkingestionitem";
        var bulkIngestionItemParentLookupColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemParentLookupColumnName") ?? "voa_parentbulkingestion";
        var ssuIdColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemSSUIdColumnName") ?? "voa_ssuid";
        var sourceValueColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemSourceValueColumnName") ?? "voa_sourcevalue";
        var validationStatusColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemValidationStatusColumnName") ?? "voa_validationstatus";
        var validationMessageColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemValidationMessageColumnName") ?? "voa_validationfailurereason";
        var requestLookupColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemRequestLookupColumnName") ?? "voa_requestlookup";
        var jobLookupColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemJobLookupColumnName") ?? "voa_joblookup";
        var processingStageColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemProcessingStageColumnName") ?? "voa_processingstage";
        var processingRunIdColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemProcessingRunIdColumnName") ?? "voa_processingrunid";
        var processingTimestampColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemProcessingTimestampColumnName") ?? "voa_processingtimestamp";
        var processingAttemptCountColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemProcessingAttemptCountColumnName") ?? "voa_processingattemptcount";

        var validStatus = Environment.GetEnvironmentVariable("BulkIngestionItemValidStatus") ?? "Valid";
        var processedStatus = Environment.GetEnvironmentVariable("BulkIngestionItemProcessedStatus") ?? "Processed";
        var failedStatus = Environment.GetEnvironmentVariable("BulkIngestionItemFailedStatus") ?? "Failed";

        var validItemsQuery = new QueryExpression(bulkIngestionItemEntityName)
        {
            ColumnSet = new ColumnSet(ssuIdColumnName, sourceValueColumnName, processingAttemptCountColumnName),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression(bulkIngestionItemParentLookupColumnName, ConditionOperator.Equal, bulkProcessorId),
                    new ConditionExpression(validationStatusColumnName, ConditionOperator.Equal, validStatus),
                }
            }
        };

        var validItems = await _dataverseService.RetrieveMultipleAsync(validItemsQuery);
        var createItems = validItems.Entities
            .Select(entity => (
                EntityId: entity.Id,
                AttemptCount: entity.GetAttributeValue<int?>(processingAttemptCountColumnName) ?? 0,
                Payload: new RequestJobCreateItem
                {
                    ItemId = entity.Id,
                    SsuId = entity.GetAttributeValue<string>(ssuIdColumnName) ?? string.Empty,
                    SourceType = sourceType,
                    SourceValue = entity.GetAttributeValue<string>(sourceValueColumnName) ?? string.Empty,
                }))
            .Where(item => !string.IsNullOrWhiteSpace(item.Payload.SsuId))
            .ToList();

        _logger.LogInformation(
            "Performance.CreateRequestsJobsInput Batch={BulkProcessorId} ValidItemsFromDataverse={ValidItemsCount} CandidateItems={CandidateCount} CorrelationId={CorrelationId}",
            bulkProcessorId,
            validItems.Entities.Count,
            createItems.Count,
            correlationId);

        var checkCrossBatchDuplicates = GetBooleanFlag("BulkIngestionCheckCrossBatchDuplicates", false);
        var duplicateFailures = new List<RequestJobCreateResult>();
        var failedByEntityId = new Dictionary<Guid, RequestJobCreateResult>();
        var eligibleItems = new List<(Guid EntityId, int AttemptCount, RequestJobCreateItem Payload)>(createItems.Count);

        if (checkCrossBatchDuplicates)
        {
            foreach (var item in createItems)
            {
                var conflictingBatches = await CrossBatchDuplicateLookupService.FindConflictingBatchesAsync(
                    _dataverseService,
                    _logger,
                    bulkProcessorId,
                    item.Payload.SsuId,
                    bulkIngestionItemEntityName,
                    bulkIngestionItemParentLookupColumnName,
                    ssuIdColumnName,
                    bulkProcessorEntityName);

                if (conflictingBatches.Count > 0)
                {
                    var duplicateMessage = CrossBatchDuplicateMessageHelper.BuildErrorMessage(conflictingBatches);

                    duplicateFailures.Add(new RequestJobCreateResult
                    {
                        Success = false,
                        SsuId = item.Payload.SsuId,
                        SourceType = sourceType,
                        ErrorCode = "ERR_DUP_SSU_OTHER_BATCH",
                        ErrorMessage = duplicateMessage,
                        FailureStageCode = Constants.StatusCodes.StageValidation,
                    });

                    failedByEntityId[item.EntityId] = duplicateFailures[^1];
                    continue;
                }

                eligibleItems.Add(item);
            }
        }
        else
        {
            eligibleItems.AddRange(createItems);
        }

        RequestJobBatchResult createResult;
        if (eligibleItems.Count > 0)
        {
            var service = new RequestJobCreationService(_dataverseService, _logger);
            createResult = await service.CreateBatchAsync(
                eligibleItems.Select(item => item.Payload),
                submitUserId,
                componentName,
                sourceType,
                jobTypeId: jobTypeId,
                createJob: createJob);
        }
        else
        {
            createResult = new RequestJobBatchResult
            {
                Success = false,
                CreatedCount = 0,
                FailedCount = 0,
            };
        }

        if (duplicateFailures.Count > 0)
        {
            createResult.Results.AddRange(duplicateFailures);
            createResult.FailedCount += duplicateFailures.Count;
            createResult.Success = false;
        }

        var requestsCreated = createResult.Results.Count(result => result.Success && result.RequestId != Guid.Empty);
        var jobsCreated = createResult.Results.Count(result => result.Success && result.JobId != Guid.Empty);

        var outcomeBySsu = createResult.Results
            .GroupBy(result => result.SsuId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var updateRequests = new List<OrganizationRequest>();
        foreach (var item in createItems)
        {
            RequestJobCreateResult? outcome = null;

            if (!failedByEntityId.TryGetValue(item.EntityId, out var failureOutcome))
            {
                outcomeBySsu.TryGetValue(item.Payload.SsuId, out outcome);
            }
            else
            {
                outcome = failureOutcome;
            }

            if (outcome is null)
            {
                continue;
            }

            var updateEntity = new Entity(bulkIngestionItemEntityName, item.EntityId)
            {
                [validationStatusColumnName] = outcome.Success ? processedStatus : failedStatus,
                [validationMessageColumnName] = outcome.Success
                    ? (createJob ? "Request/job created successfully." : "Request created successfully in Request Only mode.")
                    : $"{outcome.ErrorCode}: {outcome.ErrorMessage}",
                [processingStageColumnName] = outcome.Success
                    ? new OptionSetValue(Constants.StatusCodes.StageCompleted)
                    : new OptionSetValue(outcome.FailureStageCode ?? Constants.StatusCodes.StageRequestCreation),
                [processingTimestampColumnName] = DateTime.UtcNow,
                [processingAttemptCountColumnName] = item.AttemptCount + 1,
            };

            if (!string.IsNullOrWhiteSpace(processingRunId))
            {
                updateEntity[processingRunIdColumnName] = processingRunId;
            }

            if (outcome.Success)
            {
                // Link to the created request via EntityReference lookup
                var requestEntityName = Environment.GetEnvironmentVariable("SvtRequestEntityLogicalName") ?? "voa_requestlineitem";
                updateEntity[requestLookupColumnName] = new EntityReference(requestEntityName, outcome.RequestId);

                if (createJob && outcome.JobId != Guid.Empty)
                {
                    var jobEntityName = Environment.GetEnvironmentVariable("SvtJobEntityLogicalName") ?? "incident";
                    updateEntity[jobLookupColumnName] = new EntityReference(jobEntityName, outcome.JobId);
                }
            }

            updateRequests.Add(DataverseBulkItemWriter.BuildUpsertRequest(updateEntity));
        }

        if (updateRequests.Count > 0)
        {
            var updateItemsSw = Stopwatch.StartNew();
            var bulkWriter = new DataverseBulkItemWriter(_dataverseService);
            await bulkWriter.ExecuteItemRequestsAsync(updateRequests);

            // Refresh parent counters after processing outcomes.
            var allItemsQuery = new QueryExpression(bulkIngestionItemEntityName)
            {
                ColumnSet = new ColumnSet(validationStatusColumnName, "voa_isduplicate"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(bulkIngestionItemParentLookupColumnName, ConditionOperator.Equal, bulkProcessorId),
                    }
                }
            };

            var allItems = await _dataverseService.RetrieveMultipleAsync(allItemsQuery);
            var recalculatedCounts = CalculateItemCounts(allItems);
            bulkWriter.UpdateBatchCounters(bulkProcessorId, recalculatedCounts);
            updateItemsSw.Stop();

            _logger.LogInformation(
                "Performance.CreateRequestsJobsItemUpdates Batch={BulkProcessorId} UpdateRequests={UpdateRequests} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
                bulkProcessorId,
                updateRequests.Count,
                updateItemsSw.ElapsedMilliseconds,
                correlationId);
        }

        createFlowSw.Stop();

        _logger.LogInformation(
            "Bulk record creation completed for {BulkProcessorId}: Created={CreatedCount}, Failed={FailedCount}, CreateJob={CreateJob}, ProcessingRunId={ProcessingRunId}, CorrelationId={CorrelationId}",
            bulkProcessorId,
            createResult.CreatedCount,
            createResult.FailedCount,
            createJob,
            processingRunId,
            correlationId);
        _logger.LogInformation(
            "Performance.CreateRequestsJobsSummary Batch={BulkProcessorId} EligibleItems={EligibleItems} DuplicateRejected={DuplicateRejected} RequestsCreated={RequestsCreated} JobsCreated={JobsCreated} Failed={FailedCount} CreateJob={CreateJob} TotalElapsedMs={TotalElapsedMs} CorrelationId={CorrelationId}",
            bulkProcessorId,
            eligibleItems.Count,
            duplicateFailures.Count,
            requestsCreated,
            jobsCreated,
            createResult.FailedCount,
            createJob,
            createFlowSw.ElapsedMilliseconds,
            correlationId);

        return createResult;
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
                return status.Equals((Constants.StatusCodes.Failed).ToString(), StringComparison.OrdinalIgnoreCase);
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
    /// A tuple of succeeded chunk count, failed chunk count, and per-chunk error messages for
    /// any chunk that exhausted retries.
    /// </returns>
    private async Task<(int SucceededChunks, int FailedChunks, List<string> Errors)> ExecuteMultipleInChunksAsync(
        List<OrganizationRequest> requests,
        int chunkSize,
        int maxRetries,
        int baseDelayMs,
        string operationName,
        string? correlationId)
    {
        var succeededChunks = 0;
        var failedChunks = 0;
        var errors = new List<string>();

        if (requests.Count == 0)
        {
            return (succeededChunks, failedChunks, errors);
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
                await RetryWithBackoffAsync(
                    async () =>
                    {
                        await _dataverseService.ExecuteAsync(chunkRequest);
                        return true;
                    },
                    $"{operationName} chunk {chunkIndex + 1}/{totalChunks}",
                    maxRetries,
                    baseDelayMs,
                    correlationId);

                chunkSw.Stop();
                succeededChunks++;

                _logger.LogInformation(
                    "ExecuteMultiple chunk succeeded. Operation={Operation} Chunk={Chunk}/{Total} Count={Count} ElapsedMs={Elapsed} CorrelationId={CorrelationId}",
                    operationName, chunkIndex + 1, totalChunks, count, chunkSw.ElapsedMilliseconds, correlationId);
            }
            catch (Exception ex)
            {
                chunkSw.Stop();
                failedChunks++;
                var errMsg = $"Chunk {chunkIndex + 1}/{totalChunks} failed after {maxRetries} retries: {ex.Message}";
                errors.Add(errMsg);

                _logger.LogError(
                    ex,
                    "ExecuteMultiple chunk failed. Operation={Operation} Chunk={Chunk}/{Total} Count={Count} ElapsedMs={Elapsed} CorrelationId={CorrelationId}",
                    operationName, chunkIndex + 1, totalChunks, count, chunkSw.ElapsedMilliseconds, correlationId);
            }
        }

        return (succeededChunks, failedChunks, errors);
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


