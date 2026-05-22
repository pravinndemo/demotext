using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.DataversePlugins;

/// <summary>
/// Custom API plug-in that creates the initial SVT tracking row for the PCF flow.
/// Register this on the unbound custom API used by the PCF, and expose output parameters for
/// svtProcessingId, correlationId, status, dispatchState, and message.
/// </summary>
public sealed class SvtCreateTrackingRowPlugin : IPlugin
{
    private const string DefaultComponentName = "SaleDetailsShell";
    private const string RequestPrefix = "SVT_Request";

    public void Execute(IServiceProvider serviceProvider)
    {
        var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService))!;
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext))!;
        var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory))!;
        var organizationService = serviceFactory.CreateOrganizationService(context.InitiatingUserId);

        var ssuId = GetStringInput(context, "ssuId");
        if (string.IsNullOrWhiteSpace(ssuId))
        {
            throw new InvalidPluginExecutionException("ssuId is required.");
        }

        var componentName = GetStringInput(context, "componentName");
        if (string.IsNullOrWhiteSpace(componentName))
        {
            componentName = DefaultComponentName;
        }

        var correlationId = GetStringInput(context, "correlationId");
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = context.CorrelationId.ToString();
        }

        var requestedBy = GetStringInput(context, "requestedBy");
        if (string.IsNullOrWhiteSpace(requestedBy))
        {
            requestedBy = context.InitiatingUserId.ToString();
        }

        var saleId = GetStringInput(context, "saleId");
        var taskId = GetStringInput(context, "taskId");
        var payloadSummary = GetStringInput(context, "payloadSummary");
        if (string.IsNullOrWhiteSpace(payloadSummary))
        {
            payloadSummary = BuildPayloadSummary(saleId, taskId, ssuId);
        }

        tracingService.Trace(
            "Creating SVT tracking row. CorrelationId={0}, SsuId={1}, ComponentName={2}, RequestedBy={3}",
            correlationId,
            ssuId,
            componentName,
            requestedBy);

        var existingRow = TryFindExistingTrackingRow(organizationService, correlationId);
        if (existingRow is not null)
        {
            var existingRecord = MapTrackingRow(existingRow);

            if (existingRecord.StatusCode is not (SvtProcessingConstants.StatusCodes.Completed or SvtProcessingConstants.StatusCodes.Processing))
            {
                var dispatchStateCode = existingRecord.DispatchStateCode ?? SvtProcessingConstants.DispatchStateCodes.NotRequested;
                if (dispatchStateCode is not (SvtProcessingConstants.DispatchStateCodes.Requested or SvtProcessingConstants.DispatchStateCodes.ReRequested))
                {
                    UpdateRequestedState(organizationService, existingRecord.Id, tracingService);
                    existingRecord.DispatchStateCode = SvtProcessingConstants.DispatchStateCodes.Requested;
                    existingRecord.DispatchState = "Requested";
                    existingRecord.StatusCode = SvtProcessingConstants.StatusCodes.Queued;
                    existingRecord.Status = "Queued";
                }
            }

            WriteOutputParameters(context, existingRecord, correlationId, "SVT tracking row already exists.");
            return;
        }

        var createdRow = new Entity(SvtProcessingConstants.EntityNames.SvtProcessing)
        {
            [SvtProcessingConstants.Fields.Name] = BuildTrackingName(saleId, taskId, correlationId),
            [SvtProcessingConstants.Fields.CorrelationId] = correlationId,
            [SvtProcessingConstants.Fields.SsuId] = ssuId,
            [SvtProcessingConstants.Fields.UserId] = requestedBy,
            [SvtProcessingConstants.Fields.ComponentName] = componentName,
            [SvtProcessingConstants.Fields.DispatchState] = new OptionSetValue(SvtProcessingConstants.DispatchStateCodes.NotRequested),
            [SvtProcessingConstants.Fields.Status] = new OptionSetValue(SvtProcessingConstants.StatusCodes.Queued),
            [SvtProcessingConstants.Fields.AttemptCount] = 0,
            [SvtProcessingConstants.Fields.RequestedOn] = DateTime.UtcNow,
            [SvtProcessingConstants.Fields.IsRetryable] = true,
            [SvtProcessingConstants.Fields.PayloadSummary] = payloadSummary,
        };

        var createdId = organizationService.Create(createdRow);
        UpdateRequestedState(organizationService, createdId, tracingService);

        var createdRecord = new SvtProcessingApiResult
        {
            Id = createdId,
            CorrelationId = correlationId,
            DispatchStateCode = SvtProcessingConstants.DispatchStateCodes.Requested,
            DispatchState = "Requested",
            StatusCode = SvtProcessingConstants.StatusCodes.Queued,
            Status = "Queued",
            SsuId = ssuId,
            UserId = requestedBy,
            ComponentName = componentName,
            Message = "SVT tracking row created successfully.",
        };

        WriteOutputParameters(context, createdRecord, correlationId, createdRecord.Message);
    }

    private static string GetStringInput(IPluginExecutionContext context, string name)
    {
        if (!context.InputParameters.TryGetValue(name, out var value) || value is null)
        {
            return string.Empty;
        }

        return value as string ?? string.Empty;
    }

    private static string BuildTrackingName(string saleId, string taskId, string correlationId)
    {
        if (!string.IsNullOrWhiteSpace(taskId))
        {
            return $"Data enhancement for task {taskId.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(saleId))
        {
            return $"Data enhancement for sale {saleId.Trim()}";
        }

        var suffix = correlationId.Length > 8 ? correlationId[..8] : correlationId;
        return $"{RequestPrefix} {suffix}";
    }

    private static string BuildPayloadSummary(string saleId, string taskId, string ssuId)
        => $"saleId={saleId.Trim()};taskId={taskId.Trim()};ssuId={ssuId.Trim()}";

    private static Entity? TryFindExistingTrackingRow(IOrganizationService organizationService, string correlationId)
    {
        var query = new QueryExpression(SvtProcessingConstants.EntityNames.SvtProcessing)
        {
            ColumnSet = new ColumnSet(
                SvtProcessingConstants.Fields.Name,
                SvtProcessingConstants.Fields.CorrelationId,
                SvtProcessingConstants.Fields.SsuId,
                SvtProcessingConstants.Fields.UserId,
                SvtProcessingConstants.Fields.ComponentName,
                SvtProcessingConstants.Fields.DispatchState,
                SvtProcessingConstants.Fields.Status,
                SvtProcessingConstants.Fields.RequestId,
                SvtProcessingConstants.Fields.JobId,
                SvtProcessingConstants.Fields.AttemptCount),
            TopCount = 1,
        };
        query.Criteria.AddCondition(SvtProcessingConstants.Fields.CorrelationId, ConditionOperator.Equal, correlationId);

        return organizationService.RetrieveMultiple(query).Entities.FirstOrDefault();
    }

    private static void UpdateRequestedState(IOrganizationService organizationService, Guid svtProcessingId, ITracingService tracingService)
    {
        var updateRow = new Entity(SvtProcessingConstants.EntityNames.SvtProcessing, svtProcessingId)
        {
            [SvtProcessingConstants.Fields.DispatchState] = new OptionSetValue(SvtProcessingConstants.DispatchStateCodes.Requested),
            [SvtProcessingConstants.Fields.Status] = new OptionSetValue(SvtProcessingConstants.StatusCodes.Queued),
            [SvtProcessingConstants.Fields.RequestedOn] = DateTime.UtcNow,
            [SvtProcessingConstants.Fields.IsRetryable] = true,
        };

        tracingService.Trace("Setting SVT tracking row {0} to requested/queued.", svtProcessingId);
        organizationService.Update(updateRow);
    }

    private static SvtProcessingApiResult MapTrackingRow(Entity entity)
    {
        return new SvtProcessingApiResult
        {
            Id = entity.Id,
            CorrelationId = entity.GetAttributeValue<string>(SvtProcessingConstants.Fields.CorrelationId),
            DispatchStateCode = entity.GetAttributeValue<OptionSetValue>(SvtProcessingConstants.Fields.DispatchState)?.Value,
            DispatchState = entity.FormattedValues.TryGetValue(SvtProcessingConstants.Fields.DispatchState, out var dispatchState)
                ? dispatchState
                : null,
            StatusCode = entity.GetAttributeValue<OptionSetValue>(SvtProcessingConstants.Fields.Status)?.Value,
            Status = entity.FormattedValues.TryGetValue(SvtProcessingConstants.Fields.Status, out var status)
                ? status
                : null,
            SsuId = entity.GetAttributeValue<string>(SvtProcessingConstants.Fields.SsuId),
            UserId = entity.GetAttributeValue<string>(SvtProcessingConstants.Fields.UserId),
            ComponentName = entity.GetAttributeValue<string>(SvtProcessingConstants.Fields.ComponentName),
            RequestId = entity.GetAttributeValue<EntityReference>(SvtProcessingConstants.Fields.RequestId)?.Id,
            JobId = entity.GetAttributeValue<EntityReference>(SvtProcessingConstants.Fields.JobId)?.Id,
            AttemptCount = entity.GetAttributeValue<int?>(SvtProcessingConstants.Fields.AttemptCount) ?? 0,
            Message = "SVT tracking row already exists.",
        };
    }

    private static void WriteOutputParameters(IPluginExecutionContext context, SvtProcessingApiResult record, string correlationId, string message)
    {
        context.OutputParameters["svtProcessingId"] = record.Id.ToString("D");
        context.OutputParameters["correlationId"] = correlationId;
        context.OutputParameters["dispatchState"] = record.DispatchState ?? string.Empty;
        context.OutputParameters["dispatchStateCode"] = record.DispatchStateCode?.ToString();
        context.OutputParameters["status"] = record.Status ?? string.Empty;
        context.OutputParameters["statusCode"] = record.StatusCode?.ToString();
        context.OutputParameters["requestId"] = record.RequestId?.ToString("D") ?? string.Empty;
        context.OutputParameters["jobId"] = record.JobId?.ToString("D") ?? string.Empty;
        context.OutputParameters["ssuId"] = record.SsuId ?? string.Empty;
        context.OutputParameters["userId"] = record.UserId ?? string.Empty;
        context.OutputParameters["componentName"] = record.ComponentName ?? string.Empty;
        context.OutputParameters["attemptCount"] = record.AttemptCount;
        context.OutputParameters["message"] = message;
    }

    private sealed class SvtProcessingApiResult
    {
        public Guid Id { get; set; }

        public string? CorrelationId { get; set; }

        public int? DispatchStateCode { get; set; }

        public string? DispatchState { get; set; }

        public int? StatusCode { get; set; }

        public string? Status { get; set; }

        public Guid? RequestId { get; set; }

        public Guid? JobId { get; set; }

        public string? SsuId { get; set; }

        public string? UserId { get; set; }

        public string? ComponentName { get; set; }

        public int AttemptCount { get; set; }

        public string Message { get; set; } = string.Empty;
    }
}
