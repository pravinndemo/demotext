# Bulk Processor Validation and Error Codes (Current)

This document captures the current validation rules and error codes implemented in the codebase.

## 1. HTTP Request Validation (`BulkDataRequestProcessor`)

### 1.1 Payload and routing validation
- `INVALID_JSON`: request body is not valid JSON.
- `INVALID_REQUEST`: request body deserialized to null.
- `INVALID_ROUTE_FOR_ENDPOINT`: payload route mode does not match endpoint.
- `BULK_PROCESSOR_ID_REQUIRED`: bulk endpoints require `bulkProcessorId`.
- `INVALID_ACTION`: non-SVT request reached processor without supported action.
- `BULK_PROCESSOR_LOOKUP_FAILED`: failed to retrieve bulk ingestion header.
- `BATCH_NOT_DRAFT`: bulk action attempted when batch status is not Draft.

### 1.2 SubmitBatch pre-checks
- `TEMPLATE_SOURCE_REQUIRED`: template missing or template `voa_format` is blank.
- `NO_ITEMS_TO_SUBMIT`: `totalRows <= 0`.
- `NO_VALID_ITEMS_TO_SUBMIT`: `validItemCount <= 0`.
- `USER_ID_REQUIRED_FOR_SUBMIT`: no valid submit user (`userId` or `requestedBy`) in immediate-create mode.
- `JOB_TYPE_REQUIRED`: no resolved job type from template/header.

### 1.3 SaveItems fatal processing error
- `SAVE_ITEMS_FAILED`: unhandled exception during save-items processing.

### 1.4 SVT tracking row validation and dispatch
- `SVT_CORRELATION_ID_REQUIRED`: correlation id is missing for the SVT tracking row.
- `SVT_TRACKING_LOOKUP_FAILED`: failed to retrieve the SVT tracking row from Dataverse.
- `SVT_ALREADY_PROCESSED`: the same correlation id already completed.
- `SVT_ALREADY_PROCESSING`: the tracking row is already in `Processing`.
- `SVT_DISPATCH_NOT_REQUESTED`: SVT plug-in trigger fired without a requested dispatch state.

## 2. Route Combination Validation (`BulkDataRouteDecisionBuilder`)

- `INVALID_COMBINATION`: illegal mix of bulk and SVT fields, or no valid combination.
- `INVALID_SVT_REQUEST`: SVT tracking mode missing `svtProcessingId`, or legacy direct SVT fields were supplied.
- `BULK_PROCESSOR_ID_REQUIRED`: `ssuIds` supplied without `bulkProcessorId`.

## 3. Item Staging Validation (`BulkItemValidator`)

Validation rules applied per item:
1. SSU ID required.
2. SSU ID must be valid GUID.
3. Optional source value required when `BulkIngestionItemRequireSourceValue=true`.
4. Duplicate SSU in same batch check.
5. Duplicate source value in same batch check.
6. Optional cross-batch duplicate check when `BulkIngestionCheckCrossBatchDuplicates=true`.

Validation error codes/messages:
- `ERR_SSU_REQUIRED`: SSU ID is required.
- `ERR_SSU_INVALID_GUID`: SSU ID must be a valid GUID.
- `ERR_SOURCE_REQUIRED`: source value is required for this batch type.
- `ERR_DUP_SSU_SAME_BATCH`: duplicate SSU ID within same batch.
- `ERR_DUP_SOURCE_SAME_BATCH`: duplicate source value within same batch.
- `ERR_DUP_SSU_OTHER_BATCH`: SSU ID already exists in another batch.

Item fields updated by validator:
- `voa_validationstatus`
- `voa_validationfailurereason`
- `voa_isduplicate`
- `voa_duplicatecategory` (when duplicate)

## 4. Request/Job Creation Errors (`RequestJobCreationService`)

Batch/single creation errors:
- `NO_RESULTS`: no result produced for single-item call.
- `INVALID_USER_FORMAT`: `userId` is not a valid GUID.
- `JOB_TYPE_NOT_FOUND`: coded reason/job type could not be resolved.
- `INVALID_SSU_FORMAT`: item SSU ID not a valid GUID.
- `CREATION_FAILED`: generic create failure exception.

## 4.1 SVT Tracking and Retry Errors

These are used by the SVT tracking row and any plug-in/function handoff logic.

- `SVT_REQUEST_FAILED`: request creation failed.
- `SVT_JOB_FAILED`: job creation failed.
- `SVT_DUPLICATE_REQUEST`: an active request/job already exists for the same SVT context.
- `SVT_NOT_RETRYABLE`: retry was requested for a non-retryable row.

## 5. Timer Processing Failure Semantics (`BulkIngestionProcessor`)

Timer does not return HTTP `Code` values, but writes per-item failure reasons and statuses.

Current timer duplicate rejection code:
- `ERR_DUP_SSU_OTHER_BATCH`

Timer failure classification:
- Transient errors -> item marked failed with `voa_canreprocess=true`.
- Permanent errors -> item marked failed with `voa_canreprocess=false`.

## 6. Statuses Used During Validation/Processing

Header (`voa_bulkingestion.statuscode`):
- Draft `358800001`
- Queued `358800002`
- Partial Success `358800003`
- Completed `358800009`
- Failed `358800012`

Item (`voa_bulkingestionitem.voa_validationstatus`):
- Pending `358800000`
- Valid `358800001`
- Invalid `358800002`
- Duplicate `358800003`
- Processed `358800004`
- Failed `358800005`

Item processing stage (`voa_processingstage`):
- Staging `358800000`
- Validation `358800001`
- Request Creation `358800002`
- Job Creation `358800003`
- Completed `358800004`

## 7. Notes for Support and Integrations

- HTTP response `Code` values are authoritative for API callers.
- Item-level `voa_validationfailurereason` is authoritative for row-level troubleshooting.
- `voa_canreprocess` indicates whether timer can retry failed rows in later cycles.
- SVT row `voa_status`, `voa_requestid`, `voa_jobid`, and `voa_errormessage` are the authoritative fields for PCF polling and support triage.
