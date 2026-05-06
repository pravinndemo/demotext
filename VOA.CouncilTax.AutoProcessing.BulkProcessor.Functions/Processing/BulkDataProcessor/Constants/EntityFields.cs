using System;
namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants;

/// <summary>
/// Dataverse entity and field logical names for bulk ingestion processing.
/// </summary>
public static class EntityFields
{
    public static class EntityNames
    {
        public const string BulkIngestion = "voa_bulkingestion";
        public const string BulkIngestionItem = "voa_bulkingestionitem";
        public const string Request = "voa_request";
        public const string Job = "incident";
        public const string HereditamentReference = "voa_ssu";
    }

    public static class BulkIngestionFields
    {
        public const string Id = "voa_bulkingestionid";
        public const string Name = "voa_name";
        public const string BatchReference = "voa_batchreference";
        public const string DelayProcessingUntil = "voa_delayprocessinguntil";
        public const string Status = "statuscode";
        public const string Source = "voa_source";
        public const string SourceFile = "voa_sourcefile";
        public const string ProcessingJobType = "voa_processingjobtype";
        public const string CreatedOn = "createdon";
        public const string StateCode = "statecode";
    }

    public static class BulkIngestionItemFields
    {
        public const string Id = "voa_bulkingestionitemid";
        public const string Name = "voa_name";
        public const string ValidationStatus = "voa_validationstatus";
        public const string ParentBulkIngestion = "voa_parentbulkingestion";
        public const string HereditamentReference = "voa_hereditament";
        public const string StateCode = "statecode";
        public const string CreatedOn = "createdon";
    }

    public static class RequestFields
    {
        public const string Id = "voa_requestid";
        public const string Ratepayer = "voa_ratepayer";
        public const string SubmittedBy = "voa_submittedby";
        public const string Status = "statuscode";
        public const string Source = "voa_source";
        public const string BulkIngestionItem = "voa_bulkingestionitem";
    }

    public static class JobFields
    {
        public const string Id = "incidentid";
        public const string Title = "title";
        public const string Status = "statuscode";
        public const string Customer = "customerid";
        public const string LinkedRequest = "voa_linkedrequest";
    }

    public static class StateCode
    {
        public const int Active = 0;
        public const int Inactive = 1;
    }

    public static class JobType
    {
        public static readonly Guid DataEnhancement = new Guid("30787a01-4259-ee11-be6f-000d3a86c49a");
    }
}

