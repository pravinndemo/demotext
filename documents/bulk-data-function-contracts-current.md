# Bulk Data Function Contracts (Current)

This document defines the current HTTP and timer contracts and behavior.

## 1. HTTP Endpoints

File: `Processing/BulkDataProcessor/Processing/T_BulkDataHttpTrigger.cs`

### 1.1 `POST /bulk-data/save-items`
- Function: `T_BulkDataSaveItemsHttpTrigger`
- Action routed: `BulkRequestAction.SaveItems`
- Purpose: stage items (selection or CSV), validate, and refresh counters.
- Does not transition batch to Queued.

### 1.2 `POST /bulk-data/submit-batch`
- Function: `T_BulkDataSubmitBatchHttpTrigger`
- Action routed: `BulkRequestAction.SubmitBatch`
- Purpose: final submit checks; optional immediate request/job creation; set batch status to Queued.

### 1.3 `POST /bulk-data/svt-single`
- Function: `T_SvtSingleHttpTrigger`
- Routed as SVT-only flow.
- Purpose: direct single-item request/job creation.

### 1.4 SVT tracking model
Target design uses a separate SVT tracking table instead of bulk staging tables:

- table: `voa_svtprocessing`
- trigger field: `voa_dispatchstate`
- lifecycle field: `voa_status`
- output fields: `voa_requestid`, `voa_jobid`, `voa_errormessage`

The intended flow is:

1. PCF updates the SVT tracking row and sets dispatch state to `Requested`.
2. Async plug-in calls Azure Function.
3. Azure Function creates the request.
4. Azure Function updates the SVT row with `requestId` and `RequestCreated`.
5. Azure Function creates the job.
6. Azure Function updates the SVT row with `jobId` and `Completed`.
7. PCF polls the SVT row until it reaches `Completed` or `Failed`.

## 2. Request Contract

Model: `BulkDataRouteDecisionRequest`

Fields:
- `bulkProcessorId: Guid`
- `sourceType?: string` (optional override; otherwise derived from template `voa_format` with route fallback)
- `ssuIds?: string[]`
- `ssuId?: string` (SVT mode)
- `userId?: string` (SVT or submit user resolution)
- `componentName?: string` (SVT metadata)
- `fileColumnName?: string` (defaults to `voa_sourcefile`)
- `requestedBy?: string` (submit user fallback)
- `correlationId?: string`

## 3. Route Combinations

Resolved by `BulkDataRouteDecisionBuilder`:
- `BULK_SELECTION`: `bulkProcessorId + ssuIds[]`
- `BULK_FILE`: `bulkProcessorId` only
- `SVT_SINGLE`: `ssuId + userId + componentName`

Invalid combinations are rejected with error `Code`.

SVT tracking rows should also reject:

- missing or duplicate `correlationId`
- missing `ssuid`
- missing `userId`
- missing `componentName`
- repeated dispatch when `voa_status` is already `Processing` or `Completed`

## 4. Response Contract

Model: `BulkDataRouteDecisionResponse`

Primary fields:
- `accepted: bool`
- `code?: string`
- `message?: string`
- `bulkProcessorId?: Guid`
- `action?: string`
- `sourceType?: string`
- `stagingStatus?: string`
- `receivedCount?: int`
- `routeMode?: string`
- `statusReason?: string`
- `statusReasonCode?: int`
- `fileType?: string` (template format label)
- `fileTypeCode?: int` (template format option value)
- `correlationId?: string`

## 5. SubmitBatch Rules

Before submit success:
- Batch must be Draft.
- Template must be selected and template `voa_format` must be set.
- At least one item in batch.
- At least one valid item.

If `BulkSubmitCreateImmediately=true`:
- Requires valid submit user.
- Requires resolved job type.
- Creates request/job records for valid items and updates item outcomes.

Status transition:
- Attempts `Draft -> Queued` (`statuscode = 358800002`).

## 6. Timer Contract

File: `Processing/BulkDataProcessor/Processing/T_BulkDataTimerTrigger.cs`

Trigger:
- `[TimerTrigger(%BulkIngestionTimerSchedule%)]`

Behavior:
- Loads queued ingestions (`statuscode=Queued`).
- Processes valid items in batches.
- Applies retry policy and per-item state tracking.
- Finalizes ingestion to `Completed`, `Delayed`, `PartialSuccess`, or `Failed`.

Note:

- SVT should not be routed through the bulk timer in the target design.
- SVT uses the separate tracking row and async plug-in handoff described above.

## 7. Retry and Processing-State Contract (Timer)

Retry:
- Max retries: 3
- Backoff: exponential from 500ms

Item processing-state fields used:
- `voa_processingstage`
- `voa_processingtimestamp`
- `voa_processingattemptcount`
- `voa_lockedforprocessing`
- `voa_canreprocess`
- `voa_validationfailurereason`

Meaning:
- `voa_canreprocess=true` means item can be retried in future runs.
- Ingestion may finalize to `Delayed` when reprocessable failures remain.

## 8. Source-of-Truth Decision

- Source type is template-driven via `voa_format`.
- Header `voa_source` is considered redundant for backend routing/creation behavior.
