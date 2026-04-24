# Bulk Processor Business/Entity Mapping App Settings

Use these as **Azure Function App Settings** after deployment.

- Scope: Optional business/entity mapping settings.
- Behavior: If not set, the app uses the code defaults shown below.

## Core Runtime and Timer Settings

Set these as part of Function App configuration in addition to mapping settings.

```text
AzureWebJobsStorage=<storage-connection-string>
FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
BulkIngestionTimerSchedule=0 0 0 * * *
```

Timer notes:

- `BulkIngestionTimerSchedule` is NCRONTAB format: `{second} {minute} {hour} {day} {month} {day-of-week}`.
- Example every 15 minutes: `0 */15 * * * *`
- Example daily at 01:30 UTC: `0 30 1 * * *`
- The function name is `T_BulkDataTimerTrigger`; to disable it via app settings: `AzureWebJobs.T_BulkDataTimerTrigger.Disabled=true`
- If you need local-time scheduling, add `WEBSITE_TIME_ZONE` on supported plans.

## App Settings (Name -> Value)

```text
BulkSubmitCreateImmediately=true
BulkIngestionCheckCrossBatchDuplicates=false
BulkIngestionItemRequireSourceValue=false
BypassBusinessLogicExecutionModes=CustomSync,CustomAsync

BulkProcessorEntityLogicalName=voa_bulkingestion
BulkProcessorStatusColumnName=statuscode
BulkProcessorCustomStatusColumnName=statuscode
BulkProcessorTotalRowsColumnName=voa_totalrows
BulkProcessorValidItemCountColumnName=voa_validitemcount
BulkProcessorInvalidItemCountColumnName=voa_invaliditemcount
BulkProcessorDuplicateItemCountColumnName=voa_duplicateitemcount
BulkProcessorProcessedItemCountColumnName=voa_processeditemcount
BulkProcessorFailedItemCountColumnName=voa_faileditemcount

BulkIngestionItemEntityLogicalName=voa_bulkingestionitem
BulkIngestionItemParentLookupColumnName=voa_parentbulkingestion
BulkIngestionItemSSUIdColumnName=voa_ssuid
BulkIngestionItemSourceValueColumnName=voa_sourcevalue
BulkIngestionItemValidationStatusColumnName=voa_validationstatus
BulkIngestionItemValidationMessageColumnName=voa_validationfailurereason
BulkIngestionItemIsDuplicateColumnName=voa_isduplicate
BulkIngestionItemDuplicateCategoryColumnName=voa_duplicatecategory
BulkIngestionItemRequestLookupColumnName=voa_requestlookup
BulkIngestionItemJobLookupColumnName=voa_joblookup
BulkIngestionItemProcessingRunIdColumnName=voa_processingrunid
BulkIngestionItemProcessingTimestampColumnName=voa_processingtimestamp
BulkIngestionItemProcessingAttemptCountColumnName=voa_processingattemptcount
BulkIngestionItemPendingStatus=Pending
BulkIngestionItemValidStatus=Valid
BulkIngestionItemProcessedStatus=Processed
BulkIngestionItemFailedStatus=Failed

BulkIngestionTemplateEntityLogicalName=voa_bulkingestiontemplate
BulkIngestionTemplateJobTypeLookupColumnName=voa_jobtypelookup
BulkIngestionTemplateCaseWorkModeColumnName=voa_caseworkmode
BulkIngestionTemplateFormatColumnName=voa_format

SvtRequestEntityLogicalName=voa_requestlineitem
SvtJobEntityLogicalName=incident
SvtRequestJobLinkColumnName=voa_incidentid
SvtDefaultRequestType=Data Enhancement

RequestCodedReasonLookupColumnName=voa_codereasonid
RequestCodedReasonEntityLogicalName=voa_codereason
RequestRequestedByLookupColumnName=voa_requestedby
RequestComponentNameColumnName=voa_componentname
RequestSourceValueColumnName=voa_sourcevalue
RequestStatusColumnName=statuscode
RequestRequestTypeLookupColumnName=voa_requesttypeid
RequestTargetDateColumnName=voa_targetdate
RequestDateReceivedColumnName=voa_datereceived
RequestTargetDateOffsetDays=1
RequestSubmittedByLookupColumnName=voa_customer2id
RequestSsuLookupColumnName=voa_statutoryspatialunitid
RequestRatepayerLookupColumnName=voa_customeraccountid
RequestProposedBillingAuthorityLookupColumnName=voa_proposedbillingauthorityid
RequestRemarksColumnName=voa_remarks

JobParentRequestColumnName=voa_requestlineitemid
JobTypeColumnName=voa_codedreason
JobRequestTypeLookupColumnName=voa_requesttypeid
JobTargetDateColumnName=voa_targetdate
JobCustomerColumnName=customerid
JobTitleColumnName=title
JobDescriptionColumnName=description
```

## Notes

- Boolean app settings accept standard `true` / `false` values (case-insensitive).
- `BulkProcessorCustomStatusColumnName` falls back to `BulkProcessorStatusColumnName` when unset.
- `RequestTargetDateOffsetDays` is parsed as integer; fallback is `1`.
- These are optional mappings; only override values when Dataverse schema names differ in your target environment.
