namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Services;

public sealed class BulkItemWriteResult
{
    public int RequestedOperationCount { get; set; }

    public int SucceededOperationCount { get; set; }

    public int FailedOperationCount { get; set; }

    public List<string> Errors { get; } = new();
}
