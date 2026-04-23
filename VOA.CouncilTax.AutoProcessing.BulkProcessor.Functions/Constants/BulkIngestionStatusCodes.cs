namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Constants;

/// <summary>
/// Status codes for bulk ingestion processing.
/// These represent Dataverse option set values for ingestion and item statuses.
/// </summary>
public static class StatusCodes
{
    // Ingestion Status Codes
    public const int Submitted = 0;      // Initial submission state
    public const int Processing = 1;     // Currently being processed
    public const int Completed = 2;      // Successfully completed
    public const int Failed = 3;         // Failed processing
    public const int PartiallyFailed = 4; // Partially completed with some failures

    // Item Status Codes
    public const int Valid = 0;          // Item is valid for processing
    public const int Processed = 1;      // Item has been processed
    public const int ItemFailed = 2;     // Item processing failed
}
