namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants;

/// <summary>
/// Status codes for bulk ingestion processing.
/// These represent actual Dataverse option set values for ingestion and item statuses.
/// 
/// Bulk Ingestion (voa_bulkingestion statuscode field):
/// - Draft (358800001) - Initial SaveItems state
/// - Queued (358800002) - SubmitBatch submitted for processing
/// - Partial Success (358800003) - Timer: some items succeeded, some failed
/// - Delayed (358800004) - Timer: Retryable (transient failures)
/// - Completed (358800009) - Timer: All items succeeded
/// - Cancelled (358800010) - User cancellation
/// - Failed (358800012) - Timer: All items failed
/// 
/// Bulk Ingestion Item (voa_bulkingestionitem voa_validationstatus choice field):
/// - Pending (358800000) - SaveItems creates in this state
/// - Valid (358800001) - BulkItemValidator: Passed validation
/// - Invalid (358800002) - BulkItemValidator: Failed validation
/// - Duplicate (358800003) - BulkItemValidator: Duplicate detected
/// - Processed (358800004) - SubmitBatch: Successfully created request/job
/// - Failed (358800005) - SubmitBatch or BulkItemValidator: Processing failed
/// </summary>
public static class StatusCodes
{
    // Bulk Ingestion (voa_bulkingestion) Status Codes
    public const int Draft = 358800001;          // Initial SaveItems state
    public const int Queued = 358800002;         // SubmitBatch submitted for processing
    public const int PartialSuccess = 358800003; // Timer: some items succeeded, some failed
    public const int Delayed = 358800004;        // Timer: Retryable (transient failures)
    public const int Completed = 358800009;      // Timer: All items succeeded
    public const int Cancelled = 358800010;      // User cancellation
    public const int Failed = 358800012;         // Timer: All items failed

    // Bulk Ingestion Item (voa_bulkingestionitem) Validation Status Codes
    public const int Pending = 358800000;        // SaveItems: Initial state
    public const int Valid = 358800001;          // BulkItemValidator: Passed validation
    public const int Invalid = 358800002;        // BulkItemValidator: Failed validation
    public const int Duplicate = 358800003;      // BulkItemValidator: Duplicate detected
    public const int Processed = 358800004;      // SubmitBatch: Successfully created request/job
    public const int ItemFailed = 358800005;     // SubmitBatch or Validator: Processing failed

    // Bulk Ingestion Item (voa_bulkingestionitem voa_processingstage choice field) Stage Codes
    public const int StageStaging = 358800000;
    public const int StageValidation = 358800001;
    public const int StageRequestCreation = 358800002;
    public const int StageJobCreation = 358800003;
    public const int StageCompleted = 358800004;

    //Bulk Ingestion Assignment Mode
    public const int Team = 358800000;
    public const int Manager = 358800001;
}

