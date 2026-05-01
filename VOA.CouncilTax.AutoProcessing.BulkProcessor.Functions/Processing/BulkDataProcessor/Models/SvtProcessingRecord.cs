using Microsoft.Xrm.Sdk;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

/// <summary>
/// Strongly typed snapshot of the SVT tracking row used by the Azure Function and PCF polling flow.
/// </summary>
public sealed class SvtProcessingRecord
{
    /// <summary>Primary key of the SVT tracking row.</summary>
    public Guid Id { get; init; }

    /// <summary>Human-readable name of the tracking row.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Unique correlation key used for idempotency and polling.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>SVT business identifier stored on the row.</summary>
    public string? SsuId { get; init; }

    /// <summary>Caller or owner context captured on the row.</summary>
    public string? UserId { get; init; }

    /// <summary>Component or source name used in request metadata.</summary>
    public string? ComponentName { get; init; }

    /// <summary>Option-set value for the dispatch trigger state.</summary>
    public int? DispatchStateCode { get; init; }

    /// <summary>Formatted label for the dispatch trigger state.</summary>
    public string? DispatchState { get; init; }

    /// <summary>Option-set value for the processing lifecycle.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Formatted label for the processing lifecycle.</summary>
    public string? Status { get; init; }

    /// <summary>Request id created by the Azure Function.</summary>
    public Guid? RequestId { get; init; }

    /// <summary>Job id created by the Azure Function.</summary>
    public Guid? JobId { get; init; }

    /// <summary>Technical failure code, if the flow failed.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Readable failure message, if the flow failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Number of attempts made to process the row.</summary>
    public int AttemptCount { get; init; }

    /// <summary>Indicates whether the row can be retried.</summary>
    public bool IsRetryable { get; init; }

    /// <summary>Lookup reference to the created request row.</summary>
    public EntityReference? RequestReference { get; init; }

    /// <summary>Lookup reference to the created job row.</summary>
    public EntityReference? JobReference { get; init; }
}
