using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using VOA.CouncilTax.AutoProcessing.Constants;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Services;

internal sealed class DirectJobCreationService
{
    private readonly IOrganizationServiceAsync2 _dataverseService;
    private readonly ILogger _logger;

    public DirectJobCreationService(IOrganizationServiceAsync2 dataverseService, ILogger logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
    }

    public Guid CreateJobForRequest(Guid requestId, RequestJobCreateItem item, Guid userId, string componentName)
    {
        var requestEntityName = Environment.GetEnvironmentVariable("SvtRequestEntityLogicalName") ?? "voa_requestlineitem";
        var requestLookupColumnName = Environment.GetEnvironmentVariable("JobParentRequestColumnName") ?? ConfigurationValues.ParentRequest;
        var jobTypeColumnName = Environment.GetEnvironmentVariable("JobTypeColumnName") ?? ConfigurationValues.JobType;
        var requestTypeColumnName = Environment.GetEnvironmentVariable("JobRequestTypeLookupColumnName") ?? "voa_requesttypeid";
        var targetDateColumnName = Environment.GetEnvironmentVariable("JobTargetDateColumnName") ?? "voa_targetdate";
        var customerColumnName = Environment.GetEnvironmentVariable("JobCustomerColumnName") ?? "customerid";
        var titleColumnName = Environment.GetEnvironmentVariable("JobTitleColumnName") ?? "title";
        var descriptionColumnName = Environment.GetEnvironmentVariable("JobDescriptionColumnName") ?? "description";
        var requestSubmittedByColumnName = Environment.GetEnvironmentVariable("RequestSubmittedByLookupColumnName") ?? "voa_customer2id";
        var requestRatepayerColumnName = Environment.GetEnvironmentVariable("RequestRatepayerLookupColumnName") ?? "voa_customeraccountid";
        var requestTargetDateColumnName = Environment.GetEnvironmentVariable("RequestTargetDateColumnName") ?? "voa_targetdate";
        var requestRemarksColumnName = Environment.GetEnvironmentVariable("RequestRemarksColumnName") ?? "voa_remarks";
        var requestProposedBillingAuthorityColumnName = Environment.GetEnvironmentVariable("RequestProposedBillingAuthorityLookupColumnName") ?? ConfigurationValues.ProposedBillingAuthority;
        var requestSsuLookupColumnName = Environment.GetEnvironmentVariable("RequestSsuLookupColumnName") ?? "voa_statutoryspatialunitid";
        var requestRequestTypeColumnName = Environment.GetEnvironmentVariable("RequestRequestTypeLookupColumnName") ?? "voa_requesttypeid";
        var requestCodedReasonLookupColumnName = Environment.GetEnvironmentVariable("RequestCodedReasonLookupColumnName") ?? "voa_codereasonid";
        var codedReasonEntityName = Environment.GetEnvironmentVariable("RequestCodedReasonEntityLogicalName") ?? "voa_codereason";

        var request = _dataverseService.Retrieve(
            requestEntityName,
            requestId,
            new ColumnSet(
                ConfigurationValues.Owner,
                requestRequestTypeColumnName,
                requestCodedReasonLookupColumnName,
                requestSsuLookupColumnName,
                requestSubmittedByColumnName,
                requestRatepayerColumnName,
                requestTargetDateColumnName,
                requestRemarksColumnName,
                requestProposedBillingAuthorityColumnName));

        var requestTypeRef = request.GetAttributeValue<EntityReference>(requestRequestTypeColumnName)
            ?? new EntityReference(ConfigurationValues.RequestTypeEntityName, ConfigurationIds.RequestTypeCouncilTax);
        var codedReasonRef = request.GetAttributeValue<EntityReference>(requestCodedReasonLookupColumnName)
            ?? throw new InvalidOperationException("Request is missing coded reason lookup.");
        var ssuRef = request.GetAttributeValue<EntityReference>(requestSsuLookupColumnName);
        var ownerRef = request.GetAttributeValue<EntityReference>(ConfigurationValues.Owner)
            ?? new EntityReference("systemuser", userId);
        var customerRef = ResolveCustomerReference(request, requestRatepayerColumnName, requestSubmittedByColumnName);
        var targetDate = request.GetAttributeValue<DateTime?>(requestTargetDateColumnName);
        var remarks = request.GetAttributeValue<string>(requestRemarksColumnName);
        var proposedBillingAuthorityRef = request.GetAttributeValue<EntityReference>(requestProposedBillingAuthorityColumnName);
        var codedReasonName = ResolveCodedReasonName(codedReasonEntityName, codedReasonRef.Id);

        if (ssuRef is not null)
        {
            UpsertHereditamentLink(ssuRef.Id);
        }

        var jobEntity = new Entity(ConfigurationValues.IncidentEntityName)
        {
            [titleColumnName] = BuildJobTitle(codedReasonName, item.SsuId),
            [descriptionColumnName] = $"{item.SourceType}-initiated job for {item.SsuId} from {componentName}",
            [ConfigurationValues.Owner] = ownerRef,
            [requestLookupColumnName] = new EntityReference(requestEntityName, requestId),
            [jobTypeColumnName] = new EntityReference(codedReasonEntityName, codedReasonRef.Id),
            [requestTypeColumnName] = requestTypeRef,
            [customerColumnName] = customerRef,
            [ConfigurationValues.ReadyForQualityChecks] = false,
        };

        if (!string.IsNullOrWhiteSpace(remarks))
        {
            jobEntity[ConfigurationValues.Remarks] = remarks;
        }

        if (targetDate.HasValue)
        {
            jobEntity[targetDateColumnName] = targetDate.Value;
        }

        if (proposedBillingAuthorityRef is not null)
        {
            jobEntity[ConfigurationValues.ProposedBillingAuthority] = proposedBillingAuthorityRef;
        }

        var jobId = _dataverseService.Create(jobEntity);
        _logger.LogInformation("Direct job created. RequestId={RequestId}, JobId={JobId}", requestId, jobId);
        return jobId;
    }

    private EntityReference ResolveCustomerReference(Entity request, string ratepayerColumnName, string submittedByColumnName)
    {
        var ratepayerRef = request.GetAttributeValue<EntityReference>(ratepayerColumnName);
        if (ratepayerRef is not null)
        {
            return new EntityReference(ratepayerRef.LogicalName, ratepayerRef.Id);
        }

        var submittedByRef = request.GetAttributeValue<EntityReference>(submittedByColumnName);
        if (submittedByRef is not null)
        {
            return new EntityReference(submittedByRef.LogicalName, submittedByRef.Id);
        }

        throw new InvalidOperationException(
            $"Request is missing both '{ratepayerColumnName}' and '{submittedByColumnName}'. Direct job creation requires customer details on the request.");
    }

    private string BuildJobTitle(string? codedReasonName, string ssuId)
    {
        var prefix = string.IsNullOrWhiteSpace(codedReasonName) ? "Bulk Job" : codedReasonName;
        return $"{prefix} - {ssuId}";
    }

    private string? ResolveCodedReasonName(string entityName, Guid codedReasonId)
    {
        try
        {
            var codedReason = _dataverseService.Retrieve(entityName, codedReasonId, new ColumnSet("voa_name"));
            return codedReason.GetAttributeValue<string>("voa_name");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to resolve coded reason name for {CodedReasonId}", codedReasonId);
            return null;
        }
    }

    private void UpsertHereditamentLink(Guid ssuId)
    {
        var request = new OrganizationRequest
        {
            RequestName = "voa_UpsertHereditamentLinkV1",
            Parameters = new ParameterCollection
            {
                { "HereditamentId", ssuId }
            }
        };

        _dataverseService.Execute(request);
    }

    public void UpdateRequestAfterDirectJobCreation(Guid requestId, Guid jobId)
    {
        var requestEntityName = Environment.GetEnvironmentVariable("SvtRequestEntityLogicalName") ?? "voa_requestlineitem";
        var requestJobLookupColumnName = Environment.GetEnvironmentVariable("SvtRequestJobLinkColumnName") ?? "voa_incidentid";
        var requestStatusColumnName = Environment.GetEnvironmentVariable("RequestStatusColumnName") ?? "statuscode";

        var updateRequest = new UpdateRequest
        {
            Target = new Entity(requestEntityName, requestId)
            {
                [requestJobLookupColumnName] = new EntityReference(ConfigurationValues.IncidentEntityName, jobId),
                [requestStatusColumnName] = new OptionSetValue(ConfigurationIds.RequestInProgressStatusCode),
            }
        };

        var executeMultiple = new ExecuteMultipleRequest
        {
            Settings = new ExecuteMultipleSettings
            {
                ContinueOnError = false,
                ReturnResponses = true,
            },
            Requests = new OrganizationRequestCollection
            {
                updateRequest,
            }
        };

        executeMultiple.Parameters.Add("BypassBusinessLogicExecution", "CustomSync,CustomAsync");
        _dataverseService.Execute(executeMultiple);
    }
}