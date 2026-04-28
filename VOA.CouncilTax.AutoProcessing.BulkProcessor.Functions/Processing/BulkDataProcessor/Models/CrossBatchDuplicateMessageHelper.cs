namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

public static class CrossBatchDuplicateMessageHelper
{
    public static string BuildErrorMessage(IEnumerable<CrossBatchDuplicateBatchInfo> conflictingBatches)
    {
        var batchList = conflictingBatches
            .Where(batch => batch.BatchId != Guid.Empty)
            .GroupBy(batch => batch.BatchId)
            .Select(group => group.First())
            .OrderBy(batch => batch.BatchName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(batch => batch.BatchId)
            .ToList();

        if (batchList.Count == 0)
        {
            return "ERR_DUP_SSU_OTHER_BATCH: SSU ID already exists in another batch.";
        }

        var batchText = string.Join(", ", batchList.Select(FormatBatch));
        return $"ERR_DUP_SSU_OTHER_BATCH: SSU ID already exists in batch(es): {batchText}. Please validate those batch(es) and delete the duplicate item(s) manually before rerunning this batch.";
    }

    private static string FormatBatch(CrossBatchDuplicateBatchInfo batch)
    {
        if (!string.IsNullOrWhiteSpace(batch.BatchName))
        {
            return $"{batch.BatchName} ({batch.BatchId:N})";
        }

        return batch.BatchId.ToString("N");
    }
}
