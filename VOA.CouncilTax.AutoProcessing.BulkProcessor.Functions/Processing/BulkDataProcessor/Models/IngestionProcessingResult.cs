namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

public sealed class IngestionProcessingResult
{
    public Guid IngestionId { get; set; }
    public int TotalItems { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<string> Errors { get; } = new();
}

