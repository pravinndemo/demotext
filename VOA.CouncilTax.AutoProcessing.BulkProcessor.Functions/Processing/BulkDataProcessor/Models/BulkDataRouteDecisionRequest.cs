namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

public sealed class BulkDataRouteDecisionRequest
{
    public Guid BulkProcessorId { get; set; }

    /// <summary>voa_source option set label from voa_bulkingestion. Use "System Entered" for PCF/selection path or "CSV" for file upload path. Takes precedence over the Dataverse column when provided.</summary>
    public string? SourceType { get; set; }

    public List<string>? SsuIds { get; set; }

    /// <summary>Single SSUID for SVT mode.</summary>
    public string? SsuId { get; set; }

    /// <summary>Caller user id for SVT mode.</summary>
    public string? UserId { get; set; }

    /// <summary>Component name for SVT mode. Persisted to request/job metadata.</summary>
    public string? ComponentName { get; set; }

    /// <summary>Dataverse file column name to read CSV from. Defaults to "sourcefile" when not supplied.</summary>
    public string? FileColumnName { get; set; }

    public string? RequestedBy { get; set; }

    public string? CorrelationId { get; set; }
}

