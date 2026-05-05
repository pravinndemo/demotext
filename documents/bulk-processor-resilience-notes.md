# Bulk Processor Resilience Notes

This note documents the concrete resilience behavior in the bulk and SVT processor.

It is intended as a reference for architecture, support, and implementation review. It describes the actual failure boundaries, idempotency rules, retry handling, and status transitions used by the current codebase.

## 1. Resilience Goals

The processor is designed to:

1. Avoid duplicate downstream work when the same input is processed more than once.
2. Preserve failed work in Dataverse so operators can inspect it later.
3. Separate retryable failures from permanent failures.
4. Allow partial success without discarding the entire batch.
5. Keep request/job ownership aligned with the bulk assignment context.

## 2. Bulk Item Lifecycle

Bulk ingestion uses a staged model rather than creating requests immediately.

### 2.1 Staging

Bulk items are first written with `voa_validationstatus = Pending`.

At this point the rows are staged, not actionable by the timer.

### 2.2 Validation

The validator evaluates the staged rows and classifies them as:

- `Valid`
- `Invalid`
- `Duplicate`

This separation means bad rows are excluded before request/job creation starts.

### 2.3 Timer pickup

The timer only processes rows that are ready for request/job creation.

Rows that are still `Pending` are not consumed by the timer.

## 3. Duplicate Protection

Duplicate protection exists at multiple points.

### 3.1 Bulk staging

The validator checks for:

- duplicate SSU IDs within the same batch
- optional duplicate source values within the same batch
- optional cross-batch duplicate SSU IDs

### 3.2 Request creation

Request creation checks for an active request with the same:

- SSU
- coded reason / job type
- active Dataverse state

If one exists, the flow does not create a second request for the same business context.

### 3.3 Job creation

Job creation checks for an active job with the same:

- SSU
- coded reason / job type

If one exists, the request is linked to the existing job rather than creating a duplicate incident.

### 3.4 SVT tracking

SVT uses `voa_correlationid` and the SVT tracking row as the idempotency boundary.

Repeated processing calls are expected to resolve to the same tracking row rather than create new rows.

## 4. Ownership Rules

The processor distinguishes between:

- who owns the resulting request/job
- who submitted or triggered the flow

### 4.1 Bulk path

For bulk ingestion, request/job ownership is derived from the staged item assignment:

- `voa_assignedteam` for team-owned work
- `voa_assignedmanager` for manager-owned work
- `ownerid` as fallback

### 4.2 SVT path

For SVT, the user context comes from the Dataverse `systemuser` GUID stored on the tracking row.

The submitter context and work ownership are not treated as the same field.

## 5. Failure Handling

The processor distinguishes between permanent and retryable failures.

### 5.1 Bulk ingestion failures

Bulk failures are written back to the item row using:

- `voa_validationstatus`
- `voa_processingstage`
- `voa_canreprocess`
- `voa_validationfailurereason`

This makes the failure visible in Dataverse instead of only in logs.

### 5.2 SVT failures

SVT failures are written back using:

- `voa_status`
- `voa_errorcode`
- `voa_errormessage`
- `voa_isretryable`

For missing billing authority resolution, the code treats the failure as permanent:

- request creation stops
- the row is marked failed
- `voa_isretryable = false`

This prevents creation of a request with incomplete billing authority data.

## 6. Retry Boundaries

Retry behavior is controlled rather than implicit.

### 6.1 Bulk

The bulk timer supports partial failure handling.

Retryable failures remain visible through item state and can be processed again later.

The separate `Delayed` status was removed from the current bulk model. Retryable work now folds into `PartialSuccess`.

The timer currently retries in two ways during the same run:

1. batch-level retry for transient Dataverse/network failures
2. single-item retry for rows that fault inside `ExecuteMultiple`

Batch sizing is controlled by `BulkTimerBatchSize` with a default of `200` rows per chunk.

When a batch is processed:

- the timer sends one `ExecuteMultiple` request for the chunk
- `ContinueOnError = true` prevents one bad row from aborting the whole chunk
- the whole batch call is retried up to 3 times with exponential backoff
- failed rows inside the chunk are retried individually with bounded concurrency

Important limitation:

- the current timer does not automatically re-query `ItemFailed + voa_canreprocess = true` rows on a later run
- `voa_canreprocess` is written for traceability and future handling, but the current pickup query still reads only `Valid` rows
- if a retryable row remains failed after the current run, it is visible in Dataverse and the parent can remain `PartialSuccess`, but it is not automatically replayed in the next timer pass unless the row is explicitly moved back into a consumable state

### 6.2 SVT

SVT retries are governed by the tracking row and the `voa_isretryable` flag.

Permanent failures are not retried automatically.

## 7. Parent Batch Finalization

The bulk parent row is finalized after processing completes.

The timer now updates the parent header based on the actual outcome of the child items.

Current final states include:

- `Processing` while work is running
- `Completed` when all eligible work succeeds
- `Failed` when processing cannot continue normally
- `PartialSuccess` when some rows succeed and some remain recoverable or blocked

The state transition is also aligned with Dataverse statecode/statuscode rules so `Completed` and `Failed` move the parent into the correct inactive state.

## 8. Error Containment

The processor does not allow one bad row to cancel the entire batch.

If a row fails:

- the failure is recorded on the row
- the remaining rows can continue
- retryable failures remain eligible for later processing

This keeps the flow operational when the batch contains mixed quality data.

## 9. Early Rejection Rules

Some inputs are rejected immediately rather than being partially processed.

Examples include:

- missing required SVT fields
- missing billing authority resolution for request creation
- malformed payloads
- invalid route combinations

These are treated as controlled failures, not silent recovery cases.

## 10. Operational Outcome

The current processor behavior provides the following resilience characteristics:

- duplicate input does not automatically create duplicate requests or jobs
- failed rows remain visible in Dataverse
- retryable and permanent failures are separated
- partial success is supported
- ownership follows the bulk assignment context
- SVT and bulk flows remain isolated

## 11. Summary

The processor is resilient because it:

- stages first
- validates before creating work
- reuses existing request/job records when possible
- records failures back into Dataverse
- separates retryable and permanent failures
- finalizes parent and tracking statuses explicitly

This is a deliberate design choice in the current code, not a generic reliability claim.
