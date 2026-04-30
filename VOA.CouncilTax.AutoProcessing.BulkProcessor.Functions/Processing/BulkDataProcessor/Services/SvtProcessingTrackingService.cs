using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Services;

/// <summary>
/// Read/write helper for the SVT tracking table.
/// PCF updates the dispatch state, and Azure Function code updates the processing lifecycle, request, and job fields.
/// </summary>
public sealed class SvtProcessingTrackingService
{
    private readonly IOrganizationServiceAsync2 _dataverseService;
    private readonly ILogger _logger;

    public SvtProcessingTrackingService(IOrganizationServiceAsync2 dataverseService, ILogger logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
    }

    /// <summary>
    /// Loads a single SVT tracking row and maps the Dataverse entity into a strongly typed record.
    /// </summary>
    public async Task<SvtProcessingRecord?> RetrieveAsync(Guid svtProcessingId, string? correlationId = null)
    {
        var entityName = GetEntityName();
        var record = await _dataverseService.RetrieveAsync(
            entityName,
            svtProcessingId,
            new ColumnSet(
                SvtProcessingConstants.Fields.Name,
                SvtProcessingConstants.Fields.CorrelationId,
                SvtProcessingConstants.Fields.SsuId,
                SvtProcessingConstants.Fields.UserId,
                SvtProcessingConstants.Fields.ComponentName,
                SvtProcessingConstants.Fields.DispatchState,
                SvtProcessingConstants.Fields.Status,
                SvtProcessingConstants.Fields.RequestId,
                SvtProcessingConstants.Fields.JobId,
                SvtProcessingConstants.Fields.ErrorCode,
                SvtProcessingConstants.Fields.ErrorMessage,
                SvtProcessingConstants.Fields.AttemptCount,
                SvtProcessingConstants.Fields.IsRetryable,
                SvtProcessingConstants.Fields.RequestedOn,
                SvtProcessingConstants.Fields.RequestCreatedOn,
                SvtProcessingConstants.Fields.JobCreatedOn,
                SvtProcessingConstants.Fields.CompletedOn));

        return MapRecord(record);
    }

    /// <summary>
    /// Marks the SVT row as actively processing and increments the attempt counter.
    /// </summary>
    public async Task MarkProcessingAsync(Guid svtProcessingId, string? correlationId)
    {
        var attemptCount = await GetIncrementedAttemptCountAsync(svtProcessingId);

        await UpdateAsync(
            svtProcessingId,
            entity =>
            {
                entity[SvtProcessingConstants.Fields.Status] = new OptionSetValue(SvtProcessingConstants.StatusCodes.Processing);
                entity[SvtProcessingConstants.Fields.AttemptCount] = attemptCount;
                entity[SvtProcessingConstants.Fields.ErrorCode] = null;
                entity[SvtProcessingConstants.Fields.ErrorMessage] = null;
            },
            correlationId);
    }

    /// <summary>
    /// Stores the created request id and moves the row to RequestCreated.
    /// </summary>
    public async Task MarkRequestCreatedAsync(Guid svtProcessingId, Guid requestId, string? correlationId)
    {
        await UpdateAsync(
            svtProcessingId,
            entity =>
            {
                entity[SvtProcessingConstants.Fields.RequestId] = new EntityReference(SvtProcessingConstants.EntityNames.Request, requestId);
                entity[SvtProcessingConstants.Fields.Status] = new OptionSetValue(SvtProcessingConstants.StatusCodes.RequestCreated);
                entity[SvtProcessingConstants.Fields.RequestCreatedOn] = DateTime.UtcNow;
            },
            correlationId);
    }

    /// <summary>
    /// Stores the request and job ids and marks the row as Completed.
    /// </summary>
    public async Task MarkCompletedAsync(Guid svtProcessingId, Guid requestId, Guid jobId, string? correlationId)
    {
        await UpdateAsync(
            svtProcessingId,
            entity =>
            {
                entity[SvtProcessingConstants.Fields.RequestId] = new EntityReference(SvtProcessingConstants.EntityNames.Request, requestId);
                entity[SvtProcessingConstants.Fields.JobId] = new EntityReference(SvtProcessingConstants.EntityNames.Job, jobId);
                entity[SvtProcessingConstants.Fields.Status] = new OptionSetValue(SvtProcessingConstants.StatusCodes.Completed);
                entity[SvtProcessingConstants.Fields.JobCreatedOn] = DateTime.UtcNow;
                entity[SvtProcessingConstants.Fields.CompletedOn] = DateTime.UtcNow;
            },
            correlationId);
    }

    /// <summary>
    /// Marks the row as Failed and records the error details for PCF and support tracing.
    /// </summary>
    public async Task MarkFailedAsync(Guid svtProcessingId, string errorCode, string errorMessage, bool isRetryable, string? correlationId)
    {
        await UpdateAsync(
            svtProcessingId,
            entity =>
            {
                entity[SvtProcessingConstants.Fields.Status] = new OptionSetValue(SvtProcessingConstants.StatusCodes.Failed);
                entity[SvtProcessingConstants.Fields.ErrorCode] = errorCode;
                entity[SvtProcessingConstants.Fields.ErrorMessage] = errorMessage;
                entity[SvtProcessingConstants.Fields.IsRetryable] = isRetryable;
                entity[SvtProcessingConstants.Fields.CompletedOn] = DateTime.UtcNow;
            },
            correlationId);
    }

    /// <summary>
    /// Moves the row into the queued/requested state before the plug-in or Azure Function begins work.
    /// </summary>
    public async Task MarkRequestedAsync(Guid svtProcessingId, string? correlationId)
    {
        await UpdateAsync(
            svtProcessingId,
            entity =>
            {
                entity[SvtProcessingConstants.Fields.DispatchState] = new OptionSetValue(SvtProcessingConstants.DispatchStateCodes.Requested);
                entity[SvtProcessingConstants.Fields.Status] = new OptionSetValue(SvtProcessingConstants.StatusCodes.Queued);
                entity[SvtProcessingConstants.Fields.RequestedOn] = DateTime.UtcNow;
                entity[SvtProcessingConstants.Fields.IsRetryable] = true;
                entity[SvtProcessingConstants.Fields.AttemptCount] = 0;
            },
            correlationId);
    }

    /// <summary>
    /// Checks whether the row has already completed so retries can short-circuit safely.
    /// </summary>
    public async Task<bool> IsAlreadyCompletedAsync(Guid svtProcessingId)
    {
        var record = await RetrieveAsync(svtProcessingId);
        return record?.StatusCode == SvtProcessingConstants.StatusCodes.Completed;
    }

    private async Task UpdateAsync(Guid svtProcessingId, Action<Entity> mutate, string? correlationId)
    {
        var entityName = GetEntityName();
        var entity = new Entity(entityName, svtProcessingId);
        mutate(entity);

        try
        {
            await _dataverseService.UpdateAsync(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update SVT processing row {SvtProcessingId}. CorrelationId={CorrelationId}",
                svtProcessingId,
                correlationId);
            throw;
        }
    }

    private async Task<int> GetIncrementedAttemptCountAsync(Guid svtProcessingId)
    {
        var record = await RetrieveAsync(svtProcessingId);
        return (record?.AttemptCount ?? 0) + 1;
    }

    private SvtProcessingRecord? MapRecord(Entity entity)
    {
        if (entity is null)
        {
            return null;
        }

        return new SvtProcessingRecord
        {
            Id = entity.Id,
            Name = entity.GetAttributeValue<string>(SvtProcessingConstants.Fields.Name) ?? string.Empty,
            CorrelationId = entity.GetAttributeValue<string>(SvtProcessingConstants.Fields.CorrelationId),
            SsuId = entity.GetAttributeValue<string>(SvtProcessingConstants.Fields.SsuId),
            UserId = entity.GetAttributeValue<string>(SvtProcessingConstants.Fields.UserId),
            ComponentName = entity.GetAttributeValue<string>(SvtProcessingConstants.Fields.ComponentName),
            DispatchStateCode = entity.GetAttributeValue<OptionSetValue>(SvtProcessingConstants.Fields.DispatchState)?.Value,
            DispatchState = entity.FormattedValues.TryGetValue(SvtProcessingConstants.Fields.DispatchState, out var dispatchState)
                ? dispatchState
                : null,
            StatusCode = entity.GetAttributeValue<OptionSetValue>(SvtProcessingConstants.Fields.Status)?.Value,
            Status = entity.FormattedValues.TryGetValue(SvtProcessingConstants.Fields.Status, out var status)
                ? status
                : null,
            RequestId = entity.GetAttributeValue<EntityReference>(SvtProcessingConstants.Fields.RequestId)?.Id,
            JobId = entity.GetAttributeValue<EntityReference>(SvtProcessingConstants.Fields.JobId)?.Id,
            ErrorCode = entity.GetAttributeValue<string>(SvtProcessingConstants.Fields.ErrorCode),
            ErrorMessage = entity.GetAttributeValue<string>(SvtProcessingConstants.Fields.ErrorMessage),
            AttemptCount = entity.GetAttributeValue<int?>(SvtProcessingConstants.Fields.AttemptCount) ?? 0,
            IsRetryable = entity.GetAttributeValue<bool?>(SvtProcessingConstants.Fields.IsRetryable) ?? true,
            RequestReference = entity.GetAttributeValue<EntityReference>(SvtProcessingConstants.Fields.RequestId),
            JobReference = entity.GetAttributeValue<EntityReference>(SvtProcessingConstants.Fields.JobId),
        };
    }

    private static string GetEntityName()
    {
        return Environment.GetEnvironmentVariable("SvtProcessingEntityLogicalName") ?? SvtProcessingConstants.EntityNames.SvtProcessing;
    }
}
