# SVT Data Model and Flow

This document defines the separate SVT tracking model used by the PCF-to-Dataverse-to-Azure Function flow.

The goal is to keep SVT out of the bulk staging tables and use a dedicated tracking row for:

- request creation
- job creation
- status polling from PCF
- retry and error traceability

## 1. Design Principles

1. SVT is a single-item flow, not a batch flow.
2. Do not reuse `Bulk Ingestion` or `Bulk Ingestion Item` for SVT.
3. The SVT row is the source of truth for current status.
4. PCF writes the trigger state, Azure Function writes the processing outcome.
5. The Azure Function must be idempotent.
6. PCF should poll the SVT row instead of waiting for the full job lifecycle inline.

## 2. Table

### 2.1 `voa_svtprocessing`

Purpose:

- store one row per SVT request
- track dispatch and processing state
- store request and job identifiers once created

| Schema Name | Display Name | Type | Required | Editable | Notes |
|---|---|---|---|---|---|
| `voa_svtprocessingid` | SVT Processing | Unique Identifier | Yes | System | Primary key |
| `voa_name` | Name | Single Line of Text | Yes | Yes | Friendly display name |
| `voa_correlationid` | Correlation Id | Single Line of Text | Yes | Yes | Unique tracking key; use an alternate key |
| `voa_ssuid` | SSU Id | Single Line of Text | Yes | Yes | SVT item identifier |
| `voa_userid` | User Id | Single Line of Text | Yes | Yes | Dataverse `systemuser` GUID passed from PCF |
| `voa_componentname` | Component Name | Single Line of Text | Yes | Yes | Source component for traceability |
| `voa_dispatchstate` | Dispatch State | Choice | Yes | Yes | Trigger flag used by PCF to request processing |
| `voa_status` | Status | Choice | Yes | System | Processing lifecycle state |
| `voa_requestid` | Request Id | Lookup or Single Line of Text | No | System | Populated after request creation |
| `voa_jobid` | Job Id | Lookup or Single Line of Text | No | System | Populated after job creation |
| `voa_errorcode` | Error Code | Single Line of Text | No | System | Last error code, if any |
| `voa_errormessage` | Error Message | Multiple Lines of Text | No | System | Last error message, if any |
| `voa_attemptcount` | Attempt Count | Whole Number | Yes | System | Retry counter, default `0` |
| `voa_requestedon` | Requested On | Date and Time | Yes | System | When PCF requested processing |
| `voa_requestcreatedon` | Request Created On | Date and Time | No | System | Set when request row is created |
| `voa_jobcreatedon` | Job Created On | Date and Time | No | System | Set when job row is created |
| `voa_completedon` | Completed On | Date and Time | No | System | Final completion time |
| `voa_isretryable` | Is Retryable | Two Options | Yes | System | Controls whether PCF can retry |
| `voa_payloadsummary` | Payload Summary | Multiple Lines of Text | No | System | Short audit snapshot only |
| `statecode` | Status | Choice | Yes | System | Standard Dataverse state column |
| `statuscode` | Status Reason | Choice | Yes | System | Standard Dataverse status reason column |

## 3. Choice Values

### 3.1 Dispatch State

This column is the trigger source for the async plug-in.

| Label | Meaning |
|---|---|
| `NotRequested` | Default state, no processing requested |
| `Requested` | PCF has asked Azure Function to process the SVT row |
| `ReRequested` | User or support has requested a retry |

### 3.2 Status

This column shows the actual processing lifecycle.

| Label | Meaning |
|---|---|
| `Queued` | Row exists and is waiting to be processed |
| `Processing` | Azure Function has started work |
| `RequestCreated` | Request has been created successfully |
| `JobCreated` | Job has been created successfully |
| `Completed` | Request and job processing finished successfully |
| `Failed` | Processing failed |

Current code writes these status values in order:

- `Queued` when PCF requests processing
- `Processing` when the Azure Function starts
- `RequestCreated` after request creation succeeds
- `Completed` after request and job creation finish successfully
- `Failed` on validation, request creation, job creation, or unexpected processing errors

Note:

- `JobCreated` is defined in the model but is not currently written by the code path. The implementation jumps from `RequestCreated` to `Completed`.

## 4. Required Validation Rules

### 4.1 Create/Update rules

- `voa_correlationid` is required and must be unique.
- `voa_ssuid` is required.
- `voa_userid` is required.
- `voa_componentname` is required.
- `voa_dispatchstate` is required.
- `voa_status` is required.
- `voa_attemptcount` defaults to `0`.
- `voa_isretryable` defaults to `true`.

### 4.2 Trigger rules

- PCF only sets `voa_dispatchstate = Requested` or `ReRequested` to trigger processing.
- Azure Function updates `voa_status`, `voa_requestid`, `voa_jobid`, timestamps, and error fields.
- PCF must not directly set `voa_requestid` or `voa_jobid`.
- The async plug-in should filter on `voa_dispatchstate` only to avoid recursion.

### 4.3 Function-side idempotency rules

Azure Function should exit without creating duplicates when any of these are already true:

- the same `voa_correlationid` has already completed
- `voa_requestid` already exists on the SVT row
- an active request/job already exists for the same SSU and business context

### 4.4 Validation before request or job creation

The SVT function validates the tracking row before it creates a request or job.

It checks:

- the SVT tracking row can be read
- `voa_dispatchstate` is `Requested` or `ReRequested`
- `voa_ssuid`, `voa_userid`, and `voa_componentname` are present
- the SVT row is not already `Processing`
- the SVT row is not already `Completed`
- an active request or job does not already exist for the same SSU and business context

If any of these checks fail, the function marks the SVT row as failed or exits without creating duplicate work, depending on the condition.

## 5. Flow

### 5.1 Happy path

1. PCF creates or updates the SVT tracking row.
2. PCF sets `voa_dispatchstate = Requested` or `ReRequested`.
3. PCF sets `voa_status = Queued`.
4. Dataverse async plug-in fires on `voa_dispatchstate`.
5. Plug-in calls `POST /bulk-data/svt-single`.
6. Azure Function reads the SVT row and validates the dispatch state, required fields, and current processing state.
7. Azure Function sets `voa_status = Processing`.
8. Azure Function creates the request record.
9. Azure Function writes `voa_requestid` and sets `voa_status = RequestCreated` when the request is newly created.
10. Azure Function creates or reuses the job record.
11. Azure Function writes `voa_jobid` and sets `voa_status = Completed`.
12. Azure Function writes `voa_completedon`.
13. PCF polls the SVT row until it reaches `Completed`.

### 5.2 Unhappy paths

| Condition | What happens | Status/result |
|---|---|---|
| Missing `svtProcessingId` | Request is rejected immediately | `SVT_PROCESSING_ID_REQUIRED` |
| SVT row cannot be read | Function returns a server error | `SVT_TRACKING_LOOKUP_FAILED` |
| `voa_dispatchstate` is not `Requested` or `ReRequested` | Processing is blocked | `SVT_DISPATCH_NOT_REQUESTED` |
| `voa_ssuid`, `voa_userid`, or `voa_componentname` is missing | SVT row is marked failed | `voa_status = Failed`, `voa_errorcode = INVALID_SVT_REQUEST`, `voa_isretryable = false` |
| Request creation fails | SVT row is marked failed | `voa_status = Failed`, `voa_errorcode` and `voa_errormessage` are set, `voa_isretryable = false` for non-retryable request errors such as `NO_PROPOSED_BILLING_AUTHORITY`, `INVALID_SSU_FORMAT`, `INVALID_USER_FORMAT`, and `JOB_TYPE_NOT_FOUND`; otherwise `true` |
| Job creation fails or any unexpected exception occurs | SVT row is marked failed | `voa_status = Failed`, `voa_errorcode = SVT_JOB_FAILED`, `voa_isretryable = true` |
| SVT row is already `Processing` | Function accepts but does not duplicate work | `SVT_ALREADY_PROCESSING` |
| SVT row is already `Completed` | Function accepts but does not duplicate work | `SVT_ALREADY_PROCESSED` |
| Request and job already exist on the row | Function accepts but does not duplicate work | `SVT_ALREADY_PROCESSED` |

### 5.3 Status write summary

The SVT row is the source of truth for UI polling:

- `Queued` means the row has been submitted for background processing.
- `Processing` means the Azure Function is actively working.
- `RequestCreated` means the request row exists.
- `Completed` means request/job work is finished.
- `Failed` means processing stopped and the row needs review or retry.

## 6. Code Surface

The current implementation adds the following SVT-specific code paths:

- Azure Function route: `POST /bulk-data/svt-single`
- Azure Function processor branch: `SVT_TRACKING` inside `BulkDataRequestProcessor`
- Dataverse tracking service: `SvtProcessingTrackingService`
- Dataverse plug-in: `SvtDispatchPlugin`

The same `POST /bulk-data/svt-single` route now supports the tracking-row flow when the payload contains `svtProcessingId`.

For detailed request/job reuse scenarios, see [`svt-request-job-scenarios.md`](c:/dev/demotext/documents/svt-request-job-scenarios.md).

## 7. Patterns To Follow

### 7.1 Keep the plug-in thin

- validate the dispatch state
- call Azure Function
- do not put request/job business logic in the plug-in

### 7.2 Keep the function idempotent

- use `voa_correlationid` as the primary deduplication key
- check the SVT row before creating request/job
- treat repeated calls as normal, not exceptional

### 7.3 Keep UI polling simple

- PCF should display the live status from `voa_status`
- PCF should show `voa_requestid` and `voa_jobid` when available
- PCF should provide a refresh/retry button for failed rows

### 7.4 Keep bulk and SVT separate

- bulk uses `voa_bulkingestion` and `voa_bulkingestionitem`
- SVT uses `voa_svtprocessing`
- do not merge these flows unless the business process becomes batch-based
