# Timer Trigger Method Flow (Developer)

This document explains the timer processing flow end-to-end, including conditions, status updates, logs, and retry behavior.

## 1. Entry Point

File: `Processing/BulkDataProcessor/Processing/T_BulkDataTimerTrigger.cs`
Function: `T_BulkDataTimerTrigger`

Sequence:
1. Log execution timestamp.
2. Read `BulkSubmitCreateImmediately`.
3. If parsed false, log warning that timer path does not exercise immediate request/job creation logic.
4. Log next schedule time when available.
5. Instantiate `BulkIngestionProcessor` and call `RunAsync()`.
6. Catch and log unhandled trigger-level exceptions.

## 2. Core Processor

File: `Processing/BulkDataProcessor/Activities/BulkIngestionProcessor.cs`
Method: `RunAsync()`

Steps:
1. Start stopwatch and generate `processingRunId`.
2. Retrieve submitted ingestions via `RetrieveSubmittedIngestionsAsync()`:
   - Query `voa_bulkingestion` where `statuscode = StatusCodes.Queued` (`358800002`).
3. If none found, log and return.
4. Loop each ingestion:
   - call `ProcessSingleIngestionAsync(ingestion, processingRunId)`
   - increment processed/failed ingestion counters
5. Emit `Performance.TimerRunSummary`.

## 3. Per-Ingestion Flow

Method: `ProcessSingleIngestionAsync(...)`

Steps:
1. Log ingestion start with `ProcessingRunId`.
2. Load valid items using `RetrieveValidItemsAsync(ingestionId)`:
   - `voa_parentbulkingestion = ingestionId`
   - `voa_validationstatus = StatusCodes.Valid` (`358800001`)
3. If no valid items:
   - log warning
   - set header `statuscode = StatusCodes.Failed` (`358800012`)
   - emit `Performance.TimerIngestionSummary` with zeroes
   - return
4. Batch valid items with `BatchSize=1000`.
5. For each batch, call `ProcessBatchWithRetryAsync(batch, batchNumber, processingRunId)`.
6. Aggregate item success/failure into `IngestionProcessingResult`.
7. Finalize ingestion by calling `FinaliseIngestionAsync(result)`.
8. Emit `Performance.TimerIngestionSummary` with counts and final status.

Final ingestion status resolution:
- all success -> `Completed` (`358800009`)
- failures with `CanReprocess=true` remaining -> `Delayed` (`358800004`)
- all failed (non-reprocessable) -> `Failed` (`358800012`)
- mixed without reprocessable backlog -> `PartialSuccess` (`358800003`)

## 4. Per-Batch Flow

Method: `ProcessBatchWithRetryAsync(...)`

### 4.1 Optional cross-batch duplicate filter

If `BulkIngestionCheckCrossBatchDuplicates=true`:
1. For each item in input batch:
   - set processing state to `Validation` stage
   - set `voa_lockedforprocessing = true`
   - increment `voa_processingattemptcount`
   - read SSU from `voa_ssuid`
   - read parent from `voa_parentbulkingestion`
   - check other batches via `SsuIdExistsInOtherBatchesAsync(...)`
2. If duplicate across batches:
   - call `TryMarkItemAsFailedAsync(itemId)` -> sets:
     - `voa_validationstatus = ItemFailed`
     - `voa_processingstage = Validation`
     - `voa_validationfailurereason`
     - `voa_canreprocess = false`
     - `voa_lockedforprocessing = false`
   - append failed result with error `ERR_DUP_SSU_OTHER_BATCH`
   - do not include in eligible execution list
3. Continue with eligible items only.

If duplicate check is disabled:
- each item starts directly in `Request Creation` stage with lock=true and attempt incremented.

If all items rejected by duplicate gate:
- emit `Performance.TimerBatchSummary` and return.

### 4.2 Main execution and retries

1. Execute eligible items as one `ExecuteMultiple` request, wrapped with `RetryAsync(...)`.
2. For each response:
   - no fault -> mark success in result list
   - fault -> put item in `failedItems` for per-item retry
3. For `failedItems`, call `RetrySingleItemAsync(...)` in parallel.
4. Emit `Performance.TimerBatchSummary` with:
   - input count
   - eligible count
   - cross-batch rejected count
   - success/failure totals
   - elapsed time

## 5. Item Update Behavior in Timer

Method: `BuildItemRequest(...)`
- Success path update sets:
   - `voa_validationstatus = Processed`
   - `voa_processingstage = Completed`
   - `voa_processingtimestamp = UtcNow`
   - `voa_lockedforprocessing = false`
   - `voa_canreprocess = false`
   - clears `voa_validationfailurereason`

Method: `TryMarkItemAsFailedAsync(...)`
- Failure path update sets:
   - `voa_validationstatus = ItemFailed`
   - stage to the failed stage (`Validation` or `Request Creation`)
   - `voa_validationfailurereason`
   - `voa_processingtimestamp = UtcNow`
   - `voa_canreprocess` based on failure type
   - `voa_lockedforprocessing = false`

Method: `UpdateItemProcessingStateAsync(...)`
- Used to stamp stage transitions and lock state during processing:
   - `voa_processingstage`
   - `voa_processingtimestamp`
   - `voa_lockedforprocessing`
   - `voa_canreprocess`
   - optional attempt increment and failure-reason write

## 6. Finalization and Status Writes

Method: `FinaliseIngestionAsync(...)`
- Writes header `statuscode` based on aggregated result.
- Uses retry wrapper when writing final status.

Method: `UpdateIngestionStatusAsync(...)`
- Helper to write header `statuscode` with retry wrapper.

## 7. Logging in Timer Flow

Trigger-level logs:
- trigger execution time
- schedule-next time
- warning when `BulkSubmitCreateImmediately=false`

Processor logs:
- ingestion discovery count
- per-ingestion start
- per-batch count logs
- per-item retry warning logs for initial batch faults

Performance logs (structured):
- `Performance.TimerRunSummary`
- `Performance.TimerIngestionSummary`
- `Performance.TimerBatchSummary`

All performance logs include `ProcessingRunId` for correlation.

## 8. Retry Logic (Timer Path)

Yes, retry logic is implemented in timer flow.

Where retries exist:
1. `RetryAsync<T>(...)`
   - Used around batch execute and status updates.
   - Max retries: `MaxRetries = 3`
   - Backoff: exponential (`500ms`, `1000ms`, `2000ms`)
2. `RetrySingleItemAsync(...)`
   - Retries each failed item individually via `RetryAsync`.
3. `FinaliseIngestionAsync(...)` and `UpdateIngestionStatusAsync(...)`
   - Status writes also use `RetryAsync`.

Transient/permanent handling:
- Retry exhaustion now classifies exceptions as transient/permanent.
- Transient exhaustion leaves `voa_canreprocess=true`.
- Permanent exhaustion sets `voa_canreprocess=false`.
- Ingestion is finalized to `Delayed` when failed+reprocessable items still exist.

What retry does not guarantee:
- A permanently failing item may still fail after all retries and is then marked failed.

## 9. Key Conditions and Branches Summary

- No queued ingestions -> immediate return.
- No valid items in ingestion -> ingestion marked failed.
- Duplicate check enabled -> cross-batch duplicates rejected before execute.
- Eligible batch empty after duplicate filtering -> no execute call.
- Execute faults -> per-item retry path.
- Final status determined only after full ingestion aggregation.
