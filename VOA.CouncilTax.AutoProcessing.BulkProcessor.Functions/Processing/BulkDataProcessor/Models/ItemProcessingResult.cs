namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

public sealed class ItemProcessingResult
{
    public Guid ItemId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

