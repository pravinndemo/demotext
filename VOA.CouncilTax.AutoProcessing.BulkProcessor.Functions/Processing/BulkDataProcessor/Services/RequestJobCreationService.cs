using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System.Diagnostics;
using VOA.CouncilTax.AutoProcessing.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;


namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Services;

/// <summary>
/// Unified service for creating request and job records for both single-item and batch flows.
/// </summary>
public sealed class RequestJobCreationService
{
    private const int RetryMaxRetries = 3;
    private const int RetryBaseDelayMs = 500;

    private readonly IOrganizationServiceAsync2 _dataverseService;
    private readonly ILogger _logger;
    private readonly DirectJobCreationService _directJobCreationService;

    public RequestJobCreationService(IOrganizationServiceAsync2 dataverseService, ILogger logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
        _directJobCreationService = new DirectJobCreationService(dataverseService, logger);
    }

    /// <summary>
    /// Creates request/job records for a single item.
    /// </summary>
    public async Task<RequestJobCreateResult> CreateSingleAsync(
        RequestJobCreateItem item,
        string userId,
        string componentName,
        string? sourceType = null,
        string? defaultRequestType = null,
        Guid? jobTypeId = null,
        bool createJob = true)
    {
        var batchResult = await CreateBatchAsync(new[] { item }, userId, componentName, sourceType, defaultRequestType, jobTypeId, createJob);
        return batchResult.Results.Count == 0
            ? new RequestJobCreateResult
            {
                Success = false,
                ErrorCode = "NO_RESULTS",
                ErrorMessage = "No creation result was produced.",
                FailureStageCode = StatusCodes.StageRequestCreation,
            }
            : batchResult.Results[0];
    }

    /// <summary>
    /// Creates request/job records for a batch of items.
    /// </summary>
    public async Task<RequestJobBatchResult> CreateBatchAsync(
        IEnumerable<RequestJobCreateItem> items,
        string userId,
        string componentName,
        string? sourceType = null,
        string? defaultRequestType = null,
        Guid? jobTypeId = null,
        bool createJob = true)
    {
        var createBatchSw = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(items);

        var itemList = items.ToList();
        var results = new List<RequestJobCreateResult>(itemList.Count);

        if (!Guid.TryParse(userId, out var userIdGuid))
        {
            return new RequestJobBatchResult
            {
                Success = false,
                ErrorCode = "INVALID_USER_FORMAT",
                ErrorMessage = "User ID must be a valid GUID.",
                Results = itemList.Select(item => new RequestJobCreateResult
                {
                    Success = false,
                    SsuId = item.SsuId,
                    SourceType = sourceType ?? item.SourceType ?? "SVT",
                    ErrorCode = "INVALID_USER_FORMAT",
                    ErrorMessage = "User ID must be a valid GUID.",
                    FailureStageCode = StatusCodes.StageRequestCreation,
                }).ToList(),
            };
        }

        var requestEntityName =
    Environment.GetEnvironmentVariable("SvtRequestEntityLogicalName") ?? "voa_requestlineitem";
var requestCodedReasonLookupColumnName =
    Environment.GetEnvironmentVariable("RequestCodedReasonLookupColumnName") ?? "voa_codedreasonid";
var requestCodedReasonEntityLogicalName =
    Environment.GetEnvironmentVariable("RequestCodedReasonEntityLogicalName") ?? "voa_codedreason";
var requestSubmittingInternalUserLookupColumnName =
    Environment.GetEnvironmentVariable("RequestSubmittingInternalUserLookupColumnName") ?? "voa_submittinginternaluserid";
var requestComponentNameColumnName =
    Environment.GetEnvironmentVariable("RequestComponentNameColumnName") ?? "voa_remarks";
var requestSourceValueColumnName =
    Environment.GetEnvironmentVariable("RequestSourceValueColumnName") ?? "voa_sourcevalue";
var requestStatusColumnName =
    Environment.GetEnvironmentVariable("RequestStatusColumnName") ?? "statuscode";
        // If jobTypeId not provided, resolve by name from environment/parameter
        Guid resolvedJobTypeId = jobTypeId ?? Guid.Empty;
        if (resolvedJobTypeId == Guid.Empty)
        {
            var jobTypeName = defaultRequestType
                ?? Environment.GetEnvironmentVariable("SvtDefaultRequestType")
                ?? "Data Enhancement";

            resolvedJobTypeId = EntityFields.JobType.DataEnhancement; // Default to known Job Type if not provided and cannot resolve by name
            if (resolvedJobTypeId == Guid.Empty)
            {
                return new RequestJobBatchResult
                {
                    Success = false,
                    ErrorCode = "JOB_TYPE_NOT_FOUND",
                    ErrorMessage = $"Job type '{jobTypeName}' not found.",
                    Results = itemList.Select(item => new RequestJobCreateResult
                    {
                        Success = false,
                        SsuId = item.SsuId,
                        SourceType = sourceType ?? item.SourceType ?? "SVT",
                        ErrorCode = "JOB_TYPE_NOT_FOUND",
                        ErrorMessage = $"Job type '{jobTypeName}' not found.",
                        FailureStageCode = StatusCodes.StageRequestCreation,
                    }).ToList(),
                };
            }
        }

        foreach (var item in itemList)
        {
            var result = await CreateForItem(
                item,
                userIdGuid,
                componentName,
                sourceType,
                createJob,
                requestEntityName,
                requestCodedReasonLookupColumnName,
                requestCodedReasonEntityLogicalName,
                requestSubmittingInternalUserLookupColumnName,
                requestComponentNameColumnName,
                requestSourceValueColumnName,
                requestStatusColumnName,
                resolvedJobTypeId);
            results.Add(result);
        }

        createBatchSw.Stop();
        var requestsCreated = results.Count(result => result.Success && result.RequestId != Guid.Empty);
        var jobsCreated = results.Count(result => result.Success && result.JobId != Guid.Empty);

        _logger.LogInformation(
            "Performance.RequestJobCreateBatch ItemsInput={ItemsInput} RequestsCreated={RequestsCreated} JobsCreated={JobsCreated} Failed={Failed} CreateJob={CreateJob} ElapsedMs={ElapsedMs}",
            itemList.Count,
            requestsCreated,
            jobsCreated,
            results.Count(result => !result.Success),
            createJob,
            createBatchSw.ElapsedMilliseconds);

        return new RequestJobBatchResult
        {
            Success = results.All(result => result.Success),
            CreatedCount = results.Count(result => result.Success),
            FailedCount = results.Count(result => !result.Success),
            Results = results,
        };
    }

    private async Task<RequestJobCreateResult> CreateForItem(
        RequestJobCreateItem item,
        Guid userId,
        string componentName,
        string? sourceType,
        bool createJob,
        string requestEntityName,
        string requestCodedReasonLookupColumnName,
        string requestCodedReasonEntityLogicalName,
        string requestSubmittingInternalUserLookupColumnName,
        string requestComponentNameColumnName,
        string requestSourceValueColumnName,
        string requestStatusColumnName,
        Guid jobTypeId)
    {
        var result = new RequestJobCreateResult
        {
            SsuId = item.SsuId,
            SourceType = sourceType ?? item.SourceType ?? "SVT",
        };

        RequestJobCreateResult Fail(string errorCode, string errorMessage, int stageCode)
        {
            result.Success = false;
            result.ErrorCode = errorCode;
            result.ErrorMessage = errorMessage;
            result.FailureStageCode = stageCode;
            return result;
        }

        try
        {
            _logger.LogInformation(
                "Creating request: SsuId={SsuId}, UserId={UserId}, ComponentName={ComponentName}, SourceType={SourceType}, TriggerPluginJobCreation={CreateJob}",
                item.SsuId,
                userId,
                componentName,
                result.SourceType,
                createJob);

            if (!Guid.TryParse(item.SsuId, out var ssuIdGuid))
            {
                return Fail("INVALID_SSU_FORMAT", "SSU ID must be a valid GUID.", StatusCodes.StageRequestCreation);
            }

            if (item.ItemId.HasValue && item.ItemId.Value != Guid.Empty)
            {
                await UpdateItemProcessingStateAsync(
                    item.ItemId.Value,
                    StatusCodes.StageRequestCreation,
                    lockForProcessing: true);
            }

            bool activeRequestWithJobTypeAndHereditamentPresent =
                RetrieveActiveRequestsAsync(ssuIdGuid, jobTypeId).Result;

            if (activeRequestWithJobTypeAndHereditamentPresent)
            {
                return Fail(
                    "ACTIVE_REQUEST_PRESENT",
                    "Active requests with same SSUID and Job Type exists.",
                    StatusCodes.StageRequestCreation);
            }

            //var baReference = new RequestJobCreationService(_crmService, _logger);
            var entityForBillingAuthority =
                RetrieveBAReference(ssuIdGuid).GetAwaiter().GetResult();

            EntityReference relatedBillingAuthorityLinkRef = null;
            EntityReference proposedBillingAuthorityRef = null;
            string baReferenceNumber = string.Empty;

            if (entityForBillingAuthority != null)
            {
                relatedBillingAuthorityLinkRef =
                    entityForBillingAuthority.GetAttributeValue<EntityReference>("voa_relatedbillingauthoritylinkid") ?? null;

                proposedBillingAuthorityRef =
                    entityForBillingAuthority.GetAttributeValue<EntityReference>("voa_proposedbillingauthorityid") ?? null;

                baReferenceNumber =
                    entityForBillingAuthority.GetAttributeValue<string>("voa_bareferencenumber") ?? string.Empty;
            }

            var requestTypeColumnName =
                Environment.GetEnvironmentVariable("RequestRequestTypeLookupColumnName") ?? "voa_requesttypeid";

            var requestTargetDateColumnName =
                Environment.GetEnvironmentVariable("RequestTargetDateColumnName") ?? "voa_targetdate";

            var requestDateReceivedColumnName =
                Environment.GetEnvironmentVariable("RequestDateReceivedColumnName") ?? "voa_datereceived";

            var requestSubmittedByLookupColumnName =
                Environment.GetEnvironmentVariable("RequestSubmittedByColumnName") ?? "voa_customer2id";

            var requestRelationshipRoleLookupColumnName =
                Environment.GetEnvironmentVariable("RequestRelationshipRoleColumnName") ?? "voa_partyrelationshiproleid";

            var requestDataSourceRoleLookupColumnName =
                Environment.GetEnvironmentVariable("RequestDataSourceRoleColumnName") ?? "voa_datasourceroleid";

            var requestChannelColumnName =
                Environment.GetEnvironmentVariable("RequestChannelColumnName") ?? "voa_origincode";

            var requestRelatedBillingAuthorityLinkLookUpColumnName =
                Environment.GetEnvironmentVariable("RequestRelatedBillingAuthorityLinkLookUpColumnName")
                ?? "voa_relatedbillingauthoritylinkid";

            var requestBAReferenceNumberColumnName =
                Environment.GetEnvironmentVariable("RequestBAReferenceNumberColumnName")
                ?? "voa_bareferencenumber";

            var requestProposedBillingAuthorityIdLookUpColumnName =
                Environment.GetEnvironmentVariable("RequestProposedBillingAuthorityIdLookUpColumnName")
                ?? "voa_proposedbillingauthorityid";

            var requestTargetDateOffsetDays = GetTargetDateOffsetDays();

            var requestNameColumnName =
                Environment.GetEnvironmentVariable("RequestNameColumnName") ?? "voa_name";

            string name =
                GenerateRequestName(jobTypeId, ssuIdGuid, proposedBillingAuthorityRef.Id)
                    .GetAwaiter()
                    .GetResult();

            var requestEntity = new Entity(requestEntityName)
            {
                ["voa_statutoryspatialunitid"] =
                    new EntityReference("voa_ssu", ssuIdGuid),

                [requestCodedReasonLookupColumnName] =
                    new EntityReference(requestCodedReasonEntityLogicalName, jobTypeId),

                [requestSubmittingInternalUserLookupColumnName] =
                    new EntityReference("systemuser", userId),

                [requestSubmittedByLookupColumnName] =
                    new EntityReference(
                        "account",
                        Guid.Parse("c5812477-5367-ed11-9561-002248428304")), // Valuation Office Agency

                [requestRelationshipRoleLookupColumnName] =
                    new EntityReference(
                        ConfigurationValues.RelationshipRoleEntityName,
                        Guid.Parse("2db20153-5367-ed11-9561-002248428304")), // Valuation Office Agency

                [requestComponentNameColumnName] = componentName,

                [ConfigurationValues.Owner] =
                    new EntityReference("systemuser", userId),

                [requestTypeColumnName] =
                    new EntityReference(
                        ConfigurationValues.RequestTypeEntityName,
                        ConfigurationIds.RequestTypeCouncilTax),

                [requestDateReceivedColumnName] = DateTime.UtcNow,

                [requestTargetDateColumnName] =
                    DateTime.UtcNow.AddDays(requestTargetDateOffsetDays),

                [requestStatusColumnName] =
                    new OptionSetValue(ConfigurationIds.RequestInProgressStatusCode),

                [requestDataSourceRoleLookupColumnName] =
                    new EntityReference(
                        ConfigurationValues.DataSourceEntityName,
                        Guid.Parse("10db3bf8-f5f7-ee11-a1fe-0022481b5aad")), // Listing Officer Report

                [requestChannelColumnName] =
                    new OptionSetValue(589160010), // Manual input

                [requestNameColumnName] = name,

                [requestRelatedBillingAuthorityLinkLookUpColumnName] =
                    relatedBillingAuthorityLinkRef,

                [requestBAReferenceNumberColumnName] =
                    baReferenceNumber,

                [requestProposedBillingAuthorityIdLookUpColumnName] =
                    proposedBillingAuthorityRef
            };

            //if (!string.IsNullOrWhiteSpace(item.SourceValue))
            //{
            //    requestEntity[requestSourceValueColumnName] = item.SourceValue;
            //}

            try
            {
                result.RequestId = CreateEntityWithBypass(requestEntity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating request for SsuId={SsuId}", item.SsuId);
                return Fail("CREATION_FAILED", ex.Message, StatusCodes.StageRequestCreation);
            }

            if (createJob)
            {
                if (item.ItemId.HasValue && item.ItemId.Value != Guid.Empty)
                {
                    await UpdateItemProcessingStateAsync(
                        item.ItemId.Value,
                        StatusCodes.StageJobCreation,
                        lockForProcessing: true);
                }

                try
                {
                    result.JobId =
                        _directJobCreationService.CreateJobForRequest(
                            result.RequestId,
                            item,
                            userId,
                            componentName);

                    _directJobCreationService.UpdateRequestAfterDirectJobCreation(
                        result.RequestId,
                        result.JobId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating job for SsuId={SsuId}", item.SsuId);
                    return Fail("CREATION_FAILED", ex.Message, StatusCodes.StageJobCreation);
                }
            }

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating records for SsuId={SsuId}", item.SsuId);
            return Fail("CREATION_FAILED", ex.Message, StatusCodes.StageRequestCreation);
        }
    }

    private async Task UpdateItemProcessingStateAsync(Guid itemId, int stageCode, bool lockForProcessing)
    {
        if (itemId == Guid.Empty)
        {
            return;
        }

        try
        {
            var update = new Entity("voa_bulkingestionitem", itemId)
            {
                ["voa_processingstage"] = new OptionSetValue(stageCode),
                ["voa_processingtimestamp"] = DateTime.UtcNow,
                ["voa_lockedforprocessing"] = lockForProcessing,
                ["voa_canreprocess"] = true,
            };

            await RetryAsync(async () =>
            {
                await _dataverseService.UpdateAsync(update);
                return true;
            }, $"UpdateProcessingStage {itemId}", RetryMaxRetries);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update processing stage for item {ItemId}", itemId);
        }
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
                int delay = RetryBaseDelayMs * (int)Math.Pow(2, attempt - 1);

                _logger.LogWarning("Retry {OperationId} attempt {Attempt} - waiting {Delay}ms", operationId, attempt, delay);
                await Task.Delay(delay);
            }
        }
    }

    private async Task<Guid> GetCodedReasonIdAsync(string entityName, string valueColumnName, string targetValue)
    {
        try
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(false),
                PageInfo = new PagingInfo { PageNumber = 1, Count = 1 },
                Criteria = new FilterExpression()
                {
                    Conditions =
                    {
                        new ConditionExpression(valueColumnName, ConditionOperator.Equal, targetValue),
                    }
                }
            };

            var response = await _dataverseService.RetrieveMultipleAsync(query);
            return response.Entities.Count > 0 ? response.Entities[0].Id : Guid.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving coded reason value: {TargetValue}", targetValue);
            return Guid.Empty;
        }
    }

    private static int GetTargetDateOffsetDays()
    {
        var configuredDays = Environment.GetEnvironmentVariable("RequestTargetDateOffsetDays");
        return int.TryParse(configuredDays, out var parsedDays) ? parsedDays : 1;
    }

    private Guid CreateEntityWithBypass(Entity entity)
    {
        var createRequest = new CreateRequest
        {
            Target = entity,
        };

        createRequest.Parameters["BypassBusinessLogicExecution"] = GetBypassBusinessLogicModes();

        var response = (CreateResponse)_dataverseService.Execute(createRequest);
        return response.id;
    }

    private static string GetBypassBusinessLogicModes()
    {
        return Environment.GetEnvironmentVariable("BypassBusinessLogicExecutionModes") ?? "CustomSync,CustomAsync";
    }

    private async Task<string> GenerateRequestName(Guid jobTypeId, Guid ssuId, Guid proposedBillingAuthorityId)
    {
        _logger.LogInformation("Generating voa_name value for record");

        StringBuilder requestName = new StringBuilder("CT: Request");

        Entity codedReason = await _dataverseService.RetrieveAsync(
            "voa_codedreason",
            jobTypeId,
            new ColumnSet("voa_name"));

        string codedReasonName = codedReason.GetAttributeValue<string>("voa_name");
        requestName.Append((string.IsNullOrEmpty(codedReasonName) ? ", UNKNOWN JOB TYPE" : $", {codedReasonName}"));

        Entity hereditament = null, proposedBillingAuthority = null;

        //EntityReference hereditamentER = requestLineItem.GetAttributeValue<EntityReference>("voa_statutoryspatialunitid");
        if (ssuId != Guid.Empty)
        {
            hereditament = await _dataverseService.RetrieveAsync(
                "voa_ssu",
                ssuId,
                new ColumnSet("voa_name"));
        }

        //EntityReference proposedBillingAuthorityER = requestLineItem.GetAttributeValue<EntityReference>("voa_proposedbillingauthorityid");
        if (proposedBillingAuthorityId != Guid.Empty)
        {
            proposedBillingAuthority = await _dataverseService.RetrieveAsync(
                "account",
                proposedBillingAuthorityId,
                new ColumnSet("name"));
        }

        if (hereditament != null)
        {
            string hereditamentName = hereditament.GetAttributeValue<string>("voa_name");
            requestName.Append((string.IsNullOrEmpty(hereditamentName) ? ", Unknown or New Hereditament" : $", {hereditamentName}"));
        }
        else
        {
            requestName.Append(", Unknown or New Hereditament");
        }

        if (proposedBillingAuthority != null)
        {
            string proposedBillingAuthorityName = proposedBillingAuthority.GetAttributeValue<string>("name");
            requestName.Append((string.IsNullOrEmpty(proposedBillingAuthorityName) ? ", UNKNOWN BILLING AUTHORITY" : $", {proposedBillingAuthorityName}"));
        }

        _logger.LogInformation($"Generated Request name: '{requestName.ToString()}'");
        return requestName.ToString();
    }

    public async Task<Entity> RetrieveBAReference(Guid ssuId)
    {
        var proposedDate = DateTime.Now;
        Entity relatedBillingAuthorityLink = null;
        EntityReference proposedBillingAuthority = null;
        Entity requestLineItem = new Entity();

        if (proposedDate != DateTime.MinValue && ssuId != Guid.Empty)
        {
            relatedBillingAuthorityLink = await GetBillingAuthorityLink(_dataverseService, ssuId, proposedDate);

            if (relatedBillingAuthorityLink != null)
            {
                requestLineItem["voa_relatedbillingauthoritylinkid"] =
                    relatedBillingAuthorityLink.ToEntityReference();

                requestLineItem["voa_bareferencenumber"] =
                    relatedBillingAuthorityLink.GetAttributeValue<string>("voa_billingauthorityreference");
            }

            if (proposedBillingAuthority == null)
            {
                _logger.LogInformation(
                    $"On-Create: 'Proposed Billing Authority' is null so attempting to set it from the 'Related Billing Authority Link'");

                // If we have an active BA in the Related BA Link, then set it to this
                if (relatedBillingAuthorityLink != null &&
                    relatedBillingAuthorityLink.TryGetAttributeValue(
                        "voa_billingauthorityid",
                        out EntityReference linkedBaER))
                {
                    _logger.LogInformation(
                        "On-Create: Setting 'Proposed Billing Authority' to 'Related Billing Authority Link'");

                    proposedBillingAuthority = linkedBaER;
                    requestLineItem["voa_proposedbillingauthorityid"] = linkedBaER;
                }
            }

            // If Proposed BA is still null, try and get it from the Hereditament
            if (proposedBillingAuthority == null && ssuId != Guid.Empty)
            {
                _logger.LogInformation(
                    "On-Create: 'Proposed Billing Authority' is still null, attempting to retrieve 'Latest BA Link' from Hereditament");

                // Retrieve voa_latestbalinkid from Hereditament
                var retrievedHereditament = _dataverseService.Retrieve(
                    "voa_ssu",
                    ssuId,
                    new ColumnSet("voa_latestbalinkid"));

                if (retrievedHereditament.TryGetAttributeValue(
                    "voa_latestbalinkid",
                    out EntityReference latestBaLinkER))
                {
                    // Get the BA from the Latest BA Link
                    var retrievedLatestBaLink = _dataverseService.Retrieve(
                        latestBaLinkER.LogicalName,
                        latestBaLinkER.Id,
                        new ColumnSet("voa_billingauthorityid"));

                    if (retrievedLatestBaLink.TryGetAttributeValue(
                        "voa_billingauthorityid",
                        out EntityReference hereditamentLatestBaER))
                    {
                        _logger.LogInformation(
                            "On-Create: Setting 'Proposed Billing Authority' to 'Latest BA Link' from Hereditament");

                        proposedBillingAuthority = hereditamentLatestBaER;
                        requestLineItem["voa_proposedbillingauthorityid"] = hereditamentLatestBaER;
                    }
                }
            }

            return requestLineItem;
        }

        return null;
    }

    private async Task<Entity> GetBillingAuthorityLink(
        IOrganizationServiceAsync2 _dataverseService,
        Guid ssuId,
        DateTime proposedDate)
    {
        _logger.LogInformation($"Attempting to locate a Billing Authority Link for {ssuId}");

        if (ssuId == Guid.Empty || proposedDate == DateTime.MinValue)
        {
            return null;
        }

        var getBillingAuthorityLinkQE = new QueryExpression("voa_billingauthoritylink");
        getBillingAuthorityLinkQE.TopCount = 1;

        getBillingAuthorityLinkQE.ColumnSet.AddColumns(
            "voa_billingauthorityid",
            "voa_billingauthoritylinkid",
            "voa_billingauthorityreference",
            "voa_communitycodeid");

        getBillingAuthorityLinkQE.Criteria.AddCondition(
            "voa_statusid",
            ConditionOperator.Equal,
            new Guid("74211c0c-81b7-ed11-b596-00224801fbb2"));

        getBillingAuthorityLinkQE.Criteria.AddCondition(
            "voa_statutoryspatialunitid",
            ConditionOperator.Equal,
            ssuId);

        // BST-140261 change
        // getBillingAuthorityLinkQE.Criteria.AddCondition(
        //     "voa_effectivefrom",
        //     ConditionOperator.LessEqual,
        //     proposedDate.Date);

        getBillingAuthorityLinkQE.AddOrder("voa_createddate", OrderType.Descending);

        // Passing the plugin class name into the 'tag' request parameter means that the Virtual
        // Entity Data Provider tracing can determine which plugin triggered a Virtual Entity retrieval.
        RetrieveMultipleRequest req = new RetrieveMultipleRequest();
        req.Query = getBillingAuthorityLinkQE;
        req.Parameters.Add("tag", "GetBillingAuthorityLink");

        EntityCollection getBillingAuthorityLinkEC =
            ((RetrieveMultipleResponse)_dataverseService.Execute(req)).EntityCollection;

        return getBillingAuthorityLinkEC.Entities.FirstOrDefault();
    }
}

public sealed class RequestJobCreateItem
{
    public Guid? ItemId { get; set; }

    public string SsuId { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public string SourceValue { get; set; } = string.Empty;
    public string BaReference { get; set; } = string.Empty;
}

public sealed class RequestJobCreateResult
{
    public bool Success { get; set; }

    public string SsuId { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public Guid RequestId { get; set; }

    public Guid JobId { get; set; }

    public int? FailureStageCode { get; set; }

    public string ErrorCode { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;
}

public sealed class RequestJobBatchResult
{
    public bool Success { get; set; }

    public int CreatedCount { get; set; }

    public int FailedCount { get; set; }

    public string ErrorCode { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public List<RequestJobCreateResult> Results { get; set; } = new();
}

