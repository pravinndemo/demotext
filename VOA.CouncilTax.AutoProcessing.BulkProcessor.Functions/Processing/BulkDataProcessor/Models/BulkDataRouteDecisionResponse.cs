namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

public sealed class BulkDataRouteDecisionResponse
{
    public bool Accepted { get; set; }

    public string? Code { get; set; }

    public string? Message { get; set; }

    public Guid? BulkProcessorId { get; set; }

    public string? Action { get; set; }

    public string? SourceType { get; set; }

    public string? StagingStatus { get; set; }

    public int? ReceivedCount { get; set; }

    public List<string>? SsuIds { get; set; }

    public string? SsuId { get; set; }

    public string? UserId { get; set; }

    public string? ComponentName { get; set; }

    public string? RouteMode { get; set; }

    public string? StatusReason { get; set; }

    public int? StatusReasonCode { get; set; }

    public string? FileType { get; set; }

    public int? FileTypeCode { get; set; }

    public string? CorrelationId { get; set; }
}

