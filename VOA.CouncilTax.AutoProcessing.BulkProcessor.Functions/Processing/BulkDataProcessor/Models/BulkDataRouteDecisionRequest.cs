using Newtonsoft.Json;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

public sealed class BulkDataRouteDecisionRequest
{
    public Guid BulkProcessorId { get; set; }

    /// <summary>Source type override for routing/request creation. When omitted, source is derived from template `voa_format` and falls back by route mode (`System Entered` for selection, `CSV` for file).</summary>
    public string? SourceType { get; set; }

    public List<Ssuid>? SsuIds { get; set; }

    /// <summary>Single SSUID for SVT mode.</summary>
    public string? SsuId { get; set; }

    /// <summary>Caller user id for SVT mode.</summary>
    public string? UserId { get; set; }

    /// <summary>Component name for SVT mode. Persisted to request/job metadata.</summary>
    public string? ComponentName { get; set; }

    /// <summary>SVT tracking row id when dispatching via the new tracking flow.</summary>
    public Guid? SvtProcessingId { get; set; }

    /// <summary>Dataverse file column name to read CSV from. Defaults to "sourcefile" when not supplied.</summary>
    public string? FileColumnName { get; set; }

    public string? RequestedBy { get; set; }

    public string? CorrelationId { get; set; }
}

public class Ssuid
{
    [JsonProperty("statutorySpatialUnitId")]
    public Guid StatutorySpatialUnitId { get; set; }
}

