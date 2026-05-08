namespace VOA.Svt.Plugins.Constants;

public static class SvtProcessingConstants
{
    public static class EntityNames
    {
        public const string SvtProcessing = "voa_svtprocessing";
        public const string Request = "voa_requestlineitem";
        public const string Job = "incident";
    }

    public static class Fields
    {
        public const string Id = "voa_svtprocessingid";
        public const string Name = "voa_name";
        public const string CorrelationId = "voa_correlationid";
        public const string SsuId = "voa_ssuid";
        public const string UserId = "voa_userid";
        public const string ComponentName = "voa_componentname";
        public const string DispatchState = "voa_dispatchstate";
        public const string Status = "voa_status";
        public const string RequestId = "voa_requestid";
        public const string JobId = "voa_jobid";
        public const string ErrorCode = "voa_errorcode";
        public const string ErrorMessage = "voa_errormessage";
        public const string AttemptCount = "voa_attemptcount";
        public const string RequestedOn = "voa_requestedon";
        public const string RequestCreatedOn = "voa_requestcreatedon";
        public const string JobCreatedOn = "voa_jobcreatedon";
        public const string CompletedOn = "voa_completedon";
        public const string IsRetryable = "voa_isretryable";
        public const string PayloadSummary = "voa_payloadsummary";
        public const string StateCode = "statecode";
        public const string StatusCode = "statuscode";
    }

    public static class DispatchStateCodes
    {
        public const int NotRequested = 589160100;
        public const int Requested = 589160101;
        public const int ReRequested = 589160102;
    }

    public static class StatusCodes
    {
        public const int Queued = 589160200;
        public const int Processing = 589160201;
        public const int RequestCreated = 589160202;
        public const int JobCreated = 589160203;
        public const int Completed = 589160204;
        public const int Failed = 589160205;
    }
}
