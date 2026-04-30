# HTTP Trigger Method Flow (Developer)

This document explains the exact method flow for HTTP endpoints in bulk processing, including conditions, status updates, and logging.

## 1. Entry Points

File: `Processing/BulkDataProcessor/Processing/T_BulkDataHttpTrigger.cs`

Functions:
- `T_BulkDataSaveItemsHttpTrigger` -> `ProcessRequest(req, BulkRequestAction.SaveItems, svtOnly: false)`
- `T_BulkDataSubmitBatchHttpTrigger` -> `ProcessRequest(req, BulkRequestAction.SubmitBatch, svtOnly: false)`
- `T_SvtSingleHttpTrigger` -> `ProcessRequest(req, bulkAction: null, svtOnly: true)`

## 2. Core Dispatcher

File: `Processing/BulkDataProcessor/Activities/BulkDataRequestProcessor.cs`
Method: `ProcessRequest(...)`

### 2.1 Input parsing and route decision

Sequence:
1. Deserialize request body to `BulkDataRouteDecisionRequest`.
2. Call `BulkDataRouteDecisionBuilder.BuildDecision(request)`.
3. Validate endpoint vs route mode.

Route builder outcomes:
- `BULK_SELECTION`: `bulkProcessorId` + `ssuIds[]`
- `BULK_FILE`: `bulkProcessorId` only
- `SVT_SINGLE`: `ssuid` + `userId` + `componentName`
- Rejections:
  - Mixed bulk and SVT fields -> `INVALID_COMBINATION`
  - Incomplete SVT payload -> `INVALID_SVT_REQUEST`
  - Missing `bulkProcessorId` with `ssuIds` -> `BULK_PROCESSOR_ID_REQUIRED`

Endpoint guard outcomes:
- SVT endpoint with non-SVT payload -> `INVALID_ROUTE_FOR_ENDPOINT`
- Bulk endpoints with SVT payload -> `INVALID_ROUTE_FOR_ENDPOINT`

## 3. SVT Single Flow

The target SVT flow uses a separate tracking table and does not reuse the bulk tables.

Recommended model:

- PCF updates the SVT tracking row.
- An async plug-in watches `voa_dispatchstate`.
- The plug-in calls Azure Function.
- Azure Function creates the request first.
- Azure Function updates the SVT row to `RequestCreated`.
- Azure Function creates the job.
- Azure Function updates the SVT row to `Completed`.
- PCF polls the tracking row and refreshes the screen.

When route mode is `SVT_SINGLE` in the HTTP-triggered path:
1. Validate the incoming SVT request shape.
2. Confirm the tracking row is eligible for dispatch.
3. Create or update the request/job records.
4. Persist status and output fields back to the SVT tracking row.

Success response:
- `Action = SvtSingle`
- `StagingStatus = Completed`
- `ReceivedCount = 1`
- Message includes `RequestId`, `JobId`, and the final SVT status

Failure responses:
- Validation/business error -> `BadRequest`
- Unexpected error -> `SVT_CREATION_ERROR` (500)

## 4. Bulk Common Pre-Checks (SaveItems and SubmitBatch)

For non-SVT flow:
1. Require non-empty `bulkProcessorId`.
2. Retrieve bulk ingestion header from Dataverse:
   - `statuscode`
   - `voa_processingjobtype`
   - `voa_template`
   - count fields
3. Draft gate:
   - Allowed only when current status code equals `StatusCodes.Draft` (`358800001`).
   - Otherwise `BATCH_NOT_DRAFT`.
4. Resolve template settings:
   - Job type (`voa_jobtypelookup`)
   - Case work mode (`voa_caseworkmode`)
   - Format (`voa_format`)
5. Resolve `SourceType`:
   - request override if provided
   - else template `voa_format` label
   - else fallback by route (`CSV` for file, `System Entered` for selection)

## 5. SaveItems Flow

Method: `HandleSaveItemsAsync(...)`

### 5.1 Branching by route mode

`BULK_SELECTION` branch:
- Query existing items in batch for incoming SSU IDs.
- Build upsert requests:
  - Existing item -> update
  - Missing item -> create
- Set each item status to `Pending`.

`BULK_FILE` branch:
- Read CSV from Dataverse file column (default `voa_sourcefile`) using `CsvFileParser`.
- Parse rows into `SsuId` + `SourceRowNumber`.
- Build create/upsert requests for each row.
- Set item status `Pending`.

### 5.2 Validation and counter recompute

After upsert execution:
1. `BulkItemValidator.ValidateBatchItemsAsync(...)`
2. Validator rules per item:
   - SSU required
   - SSU must be GUID
   - optional source value required (`BulkIngestionItemRequireSourceValue`)
   - duplicate SSU in same batch
   - duplicate source value in same batch
   - optional cross-batch duplicate (`BulkIngestionCheckCrossBatchDuplicates`)
3. Item columns updated:
   - `voa_validationstatus`
   - `voa_validationfailurereason`
   - `voa_isduplicate`
   - `voa_duplicatecategory` when applicable
4. Re-read all items, compute counts, update header counters via `DataverseBulkItemWriter.UpdateBatchCounters(...)`.

Note:
- SaveItems does not transition batch to Queued.
- SaveItems does not infer deletions from missing payload rows.

## 6. SubmitBatch Flow

In `ProcessRequest` when action is `SubmitBatch`:

### 6.1 Guard conditions

Reject when:
- Template missing or template format empty -> `TEMPLATE_SOURCE_REQUIRED`
- `totalRows <= 0` -> `NO_ITEMS_TO_SUBMIT`
- `validItemCount <= 0` -> `NO_VALID_ITEMS_TO_SUBMIT`

If `BulkSubmitCreateImmediately = true`:
- Require valid submit user (`userId` or `requestedBy`) -> else `USER_ID_REQUIRED_FOR_SUBMIT`
- Require resolved job type -> else `JOB_TYPE_REQUIRED`
- Execute `CreateRequestsAndJobsForValidItemsAsync(...)`

If `BulkSubmitCreateImmediately = false`:
- Skip creation and log queue-only warning message.

### 6.2 Request/job creation internals

Method: `CreateRequestsAndJobsForValidItemsAsync(...)`

Steps:
1. Query valid items (`voa_validationstatus = Valid`).
2. Optionally reject cross-batch duplicates (`BulkIngestionCheckCrossBatchDuplicates`).
3. Call `RequestJobCreationService.CreateBatchAsync(...)` for eligible items.
4. Update each item outcome:
   - success -> `Processed`
   - failure -> `Failed`
   - set message, processing timestamp, attempt count, optional run id
   - set request lookup; job lookup if job created
5. Recalculate and update parent counters.

### 6.3 Final status transition in SubmitBatch

After creation path (or queue-only path), it attempts:
- `Draft -> Queued` by updating header `statuscode = StatusCodes.Queued` (`358800002`).

On status transition failure:
- Logs error and appends warning in response message.

## 7. Statuses and Fields Touched in HTTP Flow

Header (`voa_bulkingestion`):
- Read: `statuscode`, template/job type, counters
- Updated:
  - SaveItems: counters only
  - SubmitBatch: `statuscode -> Queued` + counters may be refreshed after item outcomes

Item (`voa_bulkingestionitem`):
- SaveItems:
  - `Pending` while staging
- Validation:
  - `Valid` / `Invalid` and duplicate metadata
- SubmitBatch:
  - `Processed` or `Failed`
  - request/job lookups
  - processing timestamp / attempts / run id

Request (`voa_requestlineitem`):
- Created in `In Progress` (`ConfigurationIds.RequestInProgressStatusCode = 1`)

Job (`incident`):
- Created only when Case Work Mode indicates request+job.

## 8. Logging in HTTP Flow

Main operational logs:
- Request receipt and payload context
- Route acceptance/rejection reasons
- Validation and staging counts
- Request/job creation results
- SVT tracking row updates and dispatch state transitions

Performance logs (structured):
- `Performance.SaveItemsExistingLookup`
- `Performance.SaveItemsCsvParse`
- `Performance.SaveItemsWrite`
- `Performance.SaveItemsValidation`
- `Performance.SaveItemsSummary`
- `Performance.CreateRequestsJobsInput`
- `Performance.CreateRequestsJobsItemUpdates`
- `Performance.CreateRequestsJobsSummary`
- `Performance.RequestJobCreateBatch`

## 9. Retry Logic (HTTP Path)

There is no explicit exponential retry loop in the HTTP orchestration path.

What exists:
- Batch write execution uses `ExecuteMultiple` with chunking and `ContinueOnError=true` (`DataverseBulkItemWriter`), but does not automatically retry failed requests.
- Per-item creation failures are captured and written back as item `Failed` with messages.

So for HTTP flow: partial failure handling exists, but no centralized retry loop like timer path.

## 10. SVT Validation Rules

SVT tracking row and dispatch validation:

1. `correlationId` is required and should be unique.
2. `ssuid`, `userId`, and `componentName` are required for SVT dispatch.
3. `voa_dispatchstate` must be set to `Requested` or `ReRequested` to trigger processing.
4. `voa_status` must not already be `Processing` or `Completed` when dispatch starts.
5. `voa_requestid` and `voa_jobid` are system-managed output fields.
6. The Azure Function must check for an existing active request/job before creating new records.
