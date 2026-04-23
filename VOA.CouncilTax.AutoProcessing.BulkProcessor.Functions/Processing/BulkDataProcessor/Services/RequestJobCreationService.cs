using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System.Diagnostics;
using VOA.CouncilTax.AutoProcessing.Constants;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Services;

/// <summary>
/// Unified service for creating request and job records for both single-item and batch flows.
/// </summary>
public sealed class RequestJobCreationService
{
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
                }).ToList(),
            };
        }

        var requestEntityName = Environment.GetEnvironmentVariable("SvtRequestEntityLogicalName") ?? "voa_requestlineitem";
        var requestCodedReasonLookupColumnName = Environment.GetEnvironmentVariable("RequestCodedReasonLookupColumnName") ?? "voa_codereasonid";
        var requestCodedReasonEntityLogicalName = Environment.GetEnvironmentVariable("RequestCodedReasonEntityLogicalName") ?? "voa_codereason";
        var requestRequestedByLookupColumnName = Environment.GetEnvironmentVariable("RequestRequestedByLookupColumnName") ?? "voa_requestedby";
        var requestComponentNameColumnName = Environment.GetEnvironmentVariable("RequestComponentNameColumnName") ?? "voa_componentname";
        var requestSourceValueColumnName = Environment.GetEnvironmentVariable("RequestSourceValueColumnName") ?? "voa_sourcevalue";
        var requestStatusColumnName = Environment.GetEnvironmentVariable("RequestStatusColumnName") ?? "statuscode";
        // If jobTypeId not provided, resolve by name from environment/parameter
        Guid resolvedJobTypeId = jobTypeId ?? Guid.Empty;
        if (resolvedJobTypeId == Guid.Empty)
        {
            var jobTypeName = defaultRequestType
                ?? Environment.GetEnvironmentVariable("SvtDefaultRequestType")
                ?? "Data Enhancement";

            resolvedJobTypeId = await GetCodedReasonIdAsync("voa_codereason", "voa_Value", jobTypeName);
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
                    }).ToList(),
                };
            }
        }

        foreach (var item in itemList)
        {
            var result = CreateForItem(
                item,
                userIdGuid,
                componentName,
                sourceType,
                createJob,
                requestEntityName,
                requestCodedReasonLookupColumnName,
                requestCodedReasonEntityLogicalName,
                requestRequestedByLookupColumnName,
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

    private RequestJobCreateResult CreateForItem(
        RequestJobCreateItem item,
        Guid userId,
        string componentName,
        string? sourceType,
        bool createJob,
        string requestEntityName,
        string requestCodedReasonLookupColumnName,
        string requestCodedReasonEntityLogicalName,
        string requestRequestedByLookupColumnName,
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

        try
        {
            _logger.LogInformation(
                "Creating request: SsuId={SsuId}, UserId={UserId}, ComponentName={ComponentName}, SourceType={SourceType}, TriggerPluginJobCreation={CreateJob}",
                item.SsuId, userId, componentName, result.SourceType, createJob);

            if (!Guid.TryParse(item.SsuId, out var ssuIdGuid))
            {
                result.Success = false;
                result.ErrorCode = "INVALID_SSU_FORMAT";
                result.ErrorMessage = "SSU ID must be a valid GUID.";
                return result;
            }

            var requestStatusCode = ConfigurationIds.RequestStatusCodeOnHold;
            var requestTypeColumnName = Environment.GetEnvironmentVariable("RequestRequestTypeLookupColumnName") ?? "voa_requesttypeid";
            var requestTargetDateColumnName = Environment.GetEnvironmentVariable("RequestTargetDateColumnName") ?? "voa_targetdate";
            var requestDateReceivedColumnName = Environment.GetEnvironmentVariable("RequestDateReceivedColumnName") ?? "voa_datereceived";
            var requestTargetDateOffsetDays = GetTargetDateOffsetDays();

            var requestEntity = new Entity(requestEntityName)
            {
                ["voa_statutoryspatialunitid"] = new EntityReference("voa_ssu", ssuIdGuid),
                [requestCodedReasonLookupColumnName] = new EntityReference(requestCodedReasonEntityLogicalName, jobTypeId),
                [requestRequestedByLookupColumnName] = new EntityReference("systemuser", userId),
                [requestComponentNameColumnName] = componentName,
                [ConfigurationValues.Owner] = new EntityReference("systemuser", userId),
                [requestTypeColumnName] = new EntityReference(ConfigurationValues.RequestTypeEntityName, ConfigurationIds.RequestTypeCouncilTax),
                [requestDateReceivedColumnName] = DateTime.UtcNow,
                [requestTargetDateColumnName] = DateTime.UtcNow.AddDays(requestTargetDateOffsetDays),
                [requestStatusColumnName] = new OptionSetValue(requestStatusCode),
            };

            if (!string.IsNullOrWhiteSpace(item.SourceValue))
            {
                requestEntity[requestSourceValueColumnName] = item.SourceValue;
            }

            result.RequestId = CreateEntityWithBypass(requestEntity);

            if (createJob)
            {
                result.JobId = _directJobCreationService.CreateJobForRequest(result.RequestId, item, userId, componentName);
                _directJobCreationService.UpdateRequestAfterDirectJobCreation(result.RequestId, result.JobId);
            }

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating records for SsuId={SsuId}", item.SsuId);
            result.Success = false;
            result.ErrorCode = "CREATION_FAILED";
            result.ErrorMessage = ex.Message;
            return result;
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
}

public sealed class RequestJobCreateItem
{
    public string SsuId { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public string SourceValue { get; set; } = string.Empty;
}

public sealed class RequestJobCreateResult
{
    public bool Success { get; set; }

    public string SsuId { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public Guid RequestId { get; set; }

    public Guid JobId { get; set; }

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

