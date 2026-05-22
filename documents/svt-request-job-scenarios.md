# SVT Request And Job Scenarios

This document explains how the SVT flow creates or reuses request and job rows, and how that compares with bulk processing.

## 1. Scope

SVT uses the following path:

- PCF writes the tracking row
- Dataverse plug-in calls the Azure Function
- Azure Function creates or reuses the request
- Azure Function creates or reuses the job
- Azure Function updates the SVT tracking row with the final identifiers and status

The SVT handler is in [`BulkDataRequestProcessorSvt.cs`](c:/dev/demotext/VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions/Processing/BulkDataProcessor/Activities/BulkDataRequestProcessorSvt.cs).

## 2. Matching Rules

The current code uses these checks:

- request dedupe: active request with the same SSU and coded reason/job type
- job dedupe: active job with the same SSU and coded reason/job type
- request linkage: if the request already has a job link, reuse it

The default job type used in this flow is `Data Enhancement` when no job type is explicitly supplied.

## 3. Validation Before Request Or Job Creation

The SVT flow validates the tracking row before it creates a request or job.

These checks run first:

- the SVT tracking row can be read
- `voa_dispatchstate` is `Requested` or `ReRequested`
- `voa_ssuid`, `voa_userid`, and `voa_componentname` are present
- the SVT row is not already `Processing`
- the SVT row is not already `Completed`
- an active request or job does not already exist for the same SSU and business context

If a check fails, the flow stops or marks the SVT row as failed, depending on the condition.

Relevant implementation:

- [`RequestJobCreationService.cs`](c:/dev/demotext/VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions/Processing/BulkDataProcessor/Services/RequestJobCreationService.cs)
- [`DirectJobCreationService.cs`](c:/dev/demotext/VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions/Processing/BulkDataProcessor/Services/DirectJobCreationService.cs)

## 4. Happy Path Scenarios

### 4.1 No existing request or job

1. SVT tracking row is accepted.
2. Function marks the SVT row `Processing`.
3. Function creates a new request.
4. Function marks the SVT row `RequestCreated`.
5. Function creates a new job.
6. Function marks the SVT row `Completed`.

Outcome:

- one request created
- one job created
- SVT row completes successfully

### 4.2 Existing active request, no job yet

1. `CreateRequestOnlyAsync(...)` detects `ACTIVE_REQUEST_PRESENT`.
2. SVT tries to recover the existing active request by SSU and component name.
3. If found, the existing request is reused.
4. The job path runs next.
5. If no active job exists for that SSU and job type, a new job is created.

Outcome:

- no duplicate request
- one job either reused or created
- SVT row completes successfully

### 4.3 Existing active job already linked to the request

1. SVT sees the request already exists.
2. SVT checks the request for an existing job link.
3. The existing job is reused.
4. No new incident is created.

Outcome:

- no duplicate job
- SVT row completes successfully

### 4.4 Existing active job for the same SSU and job type, but not linked to the request

1. Request creation succeeds or the existing request is reused.
2. Direct job creation checks for an active job with the same SSU and coded reason/job type.
3. If found, the existing job is reused and linked back to the request.
4. No duplicate job is created.

Outcome:

- no duplicate job
- request is linked to the existing active job
- SVT row completes successfully

## 5. Unhappy Path Scenarios

### 5.1 Missing or invalid SVT input

- missing `svtProcessingId`
- missing `ssuId`
- missing `userId`
- missing `componentName`
- invalid dispatch state
- row already `Processing` or `Completed`

Outcome:

- request is rejected or the SVT row is marked failed

### 5.2 Active request exists for the same SSU and job type

- request creation returns `ACTIVE_REQUEST_PRESENT`
- SVT tries to resolve the existing request
- if no active request can be recovered, the row fails

Outcome:

- no duplicate request is created
- processing may stop if the existing request cannot be reused

### 5.3 Request creation fails

- Dataverse request create throws
- SVT row is marked failed

Outcome:

- no request
- no job
- SVT row ends in `Failed`

### 5.4 Job creation fails

- request exists
- job create throws or Dataverse rejects the write

Outcome:

- SVT row is marked failed
- request may already exist
- job may not be created

## 6. Bulk Processor Parity

Yes, the bulk processor uses the same duplicate checks.

The bulk ingestion timer creates request/job records through `RequestJobCreationService` in [`BulkIngestionProcessor.cs`](c:/dev/demotext/VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions/Processing/BulkDataProcessor/Activities/BulkIngestionProcessor.cs). That service then calls the same request and job creation logic used by SVT.

That means bulk processing gets the same behavior:

- active request dedupe by SSU + job type
- active job dedupe by SSU + job type
- request-to-job reuse when a job link already exists

The difference is only the input shape:

- SVT is one tracking row at a time
- bulk is a batch of items processed through the same service

## 7. Practical Result

If you see an existing active job for the same SSU and `Data Enhancement`, the current code should reuse it instead of creating a new one.

If you want stricter behavior, the next step would be to change the job dedupe rule from:

- same SSU + same job type

to:

- same SSU only
