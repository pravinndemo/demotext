namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

public sealed class CrossBatchDuplicateBatchInfo
{
    public Guid BatchId { get; set; }

    public string BatchName { get; set; } = string.Empty;
}
