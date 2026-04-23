namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Services;

public sealed class BulkItemCounts
{
    public int TotalRows { get; set; }

    public int ValidItemCount { get; set; }

    public int InvalidItemCount { get; set; }

    public int DuplicateItemCount { get; set; }

    public int ProcessedItemCount { get; set; }

    public int FailedItemCount { get; set; }
}

