namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants;

/// <summary>
/// Dataverse logical names and status values for the SVT tracking table.
/// Dispatch state is the PCF-triggered input, while status is the processing lifecycle written by Azure Function code.
/// </summary>
public static class SvtProcessingConstants
{
    public static class EntityNames
    {
        /// <summary>SVT tracking table logical name.</summary>
        public const string SvtProcessing = "voa_svtprocessing";
        /// <summary>Request table logical name used when creating the SVT request row.</summary>
        public const string Request = "voa_requestlineitem";
        /// <summary>Job table logical name used when creating the SVT job row.</summary>
        public const string Job = "incident";
    }

    public static class Fields
    {
        /// <summary>Primary key column for the SVT tracking table.</summary>
        public const string Id = "voa_svtprocessingid";
        /// <summary>Human-readable name column on the SVT tracking row.</summary>
        public const string Name = "voa_name";
        /// <summary>Unique id used for correlation and idempotency.</summary>
        public const string CorrelationId = "voa_correlationid";
        /// <summary>SVT business identifier stored on the tracking row.</summary>
        public const string SsuId = "voa_ssuid";
        /// <summary>Caller or owner context stored with the tracking row.</summary>
        public const string UserId = "voa_userid";
        /// <summary>Component/source name used for request metadata and support tracing.</summary>
        public const string ComponentName = "voa_componentname";
        /// <summary>PCF-controlled dispatch trigger column. The plug-in listens for this change.</summary>
        public const string DispatchState = "voa_dispatchstate";
        /// <summary>Processing lifecycle column updated by Azure Function code.</summary>
        public const string Status = "voa_status";
        /// <summary>Lookup to the created request row.</summary>
        public const string RequestId = "voa_requestid";
        /// <summary>Lookup to the created job row.</summary>
        public const string JobId = "voa_jobid";
        /// <summary>Short technical error code for failures.</summary>
        public const string ErrorCode = "voa_errorcode";
        /// <summary>Readable error message for diagnostics and PCF display.</summary>
        public const string ErrorMessage = "voa_errormessage";
        /// <summary>Number of attempts made to process the SVT row.</summary>
        public const string AttemptCount = "voa_attemptcount";
        /// <summary>Timestamp when the request was accepted.</summary>
        public const string RequestedOn = "voa_requestedon";
        /// <summary>Timestamp when the request row was created.</summary>
        public const string RequestCreatedOn = "voa_requestcreatedon";
        /// <summary>Timestamp when the job row was created.</summary>
        public const string JobCreatedOn = "voa_jobcreatedon";
        /// <summary>Timestamp when the SVT processing completed.</summary>
        public const string CompletedOn = "voa_completedon";
        /// <summary>Whether the row can be retried from PCF.</summary>
        public const string IsRetryable = "voa_isretryable";
        /// <summary>Optional short audit summary of the inbound payload.</summary>
        public const string PayloadSummary = "voa_payloadsummary";
        /// <summary>System state code column.</summary>
        public const string StateCode = "statecode";
        /// <summary>System status code column.</summary>
        public const string StatusCode = "statuscode";
    }

    public static class DispatchStateCodes
    {
        /// <summary>Default state before the user asks for processing.</summary>
        public const int NotRequested = 589160100;
        /// <summary>Set by PCF to trigger the async plug-in and start processing.</summary>
        public const int Requested = 589160101;
        /// <summary>Used when the user retries a failed SVT row.</summary>
        public const int ReRequested = 589160102;
    }

    public static class StatusCodes
    {
        /// <summary>Row is queued for processing.</summary>
        public const int Queued = 589160200;
        /// <summary>Azure Function is actively processing the row.</summary>
        public const int Processing = 589160201;
        /// <summary>Request row has been created successfully.</summary>
        public const int RequestCreated = 589160202;
        /// <summary>Job row has been created successfully.</summary>
        public const int JobCreated = 589160203;
        /// <summary>All required work completed successfully.</summary>
        public const int Completed = 589160204;
        /// <summary>Processing failed and requires review or retry.</summary>
        public const int Failed = 589160205;
    }
}
