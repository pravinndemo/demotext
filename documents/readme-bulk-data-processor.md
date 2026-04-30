# README: Bulk Data Processor

This document explains the `BulkDataRequestProcessor` in plain, practical terms.

## What this processor is

`BulkDataRequestProcessor` is the orchestration class behind the bulk and SVT HTTP endpoints.

It is the traffic controller for incoming requests. It decides:

- what type of payload arrived
- whether that payload is valid for the called endpoint
- whether batch status allows the operation
- what response to return

## Why this class exists

The Azure Function trigger class should stay thin.

So we split responsibilities:

- `T_BulkDataHttpTrigger`:
  - endpoint wiring only
  - passes requests to processor
- `BulkDataRequestProcessor`:
  - request validation and orchestration
  - status gate checks
  - action-specific flow (`SaveItems`, `SubmitBatch`, `SVT_TRACKING`)
- `BulkDataRouteDecisionBuilder`:
  - payload-shape routing only

This makes code easier to maintain and test.

## Endpoints and intent

These are the supported routes:

- `POST /bulk-data/save-items`
- `POST /bulk-data/submit-batch`
- `POST /bulk-data/svt-single`

Intent mapping:

- `save-items`:
  - create/update batch items while parent batch stays in `Draft`
- `submit-batch`:
  - final submit of a `Draft` batch, create requests for valid items, then move to `Queued`
- `svt-single`:
  - tracking-row dispatch outside bulk batch staging

## Current behavior in processor

The processor currently does these steps:

1. Deserialize request JSON
- Reject invalid JSON (`INVALID_JSON`)
- Reject empty payload (`INVALID_REQUEST`)

2. Determine route mode by payload shape
- Uses `BulkDataRouteDecisionBuilder`
- Modes: `BULK_SELECTION`, `BULK_FILE`, `SVT_TRACKING`

3. Validate endpoint vs route mode
- SVT endpoint accepts only SVT payload
- Bulk endpoints reject SVT payload

4. SVT path
- Loads the SVT tracking row and creates the request/job for tracking payloads

5. Bulk path
- Requires `bulkProcessorId`
- Loads parent batch from Dataverse
- Checks batch is in `Draft`
- Adds status/file metadata into response

6. Action handling
- `SaveItems`:
  - accepted for staging in Draft
  - batch remains `Draft`
  - create/update semantics only
  - deletions are not inferred
- `SubmitBatch`:
  - accepted for final submit
  - creates requests for valid items
  - for `Request and Job(s)`, creates the incident directly and then performs a bypassed follow-up request update to avoid duplicate plugin firing
  - updates parent batch `Draft -> Queued`

## Important business rule (delete behavior)

`SaveItems` does not perform sync-delete.

If user removes items, deletion is explicit from form/subgrid actions.

This avoids accidental row removal when payload does not include all existing items.

## Dataverse integration points

Used services/classes:

- `IOrganizationServiceAsync2` for Dataverse read/write
- `DataverseBulkItemWriter` for batch operations (`ExecuteMultiple`)

`DataverseBulkItemWriter` currently provides:

- chunked create/update/delete request execution
- batch counter update helper

## What is already complete

- endpoint split (`save-items`, `submit-batch`, `svt-single`)
- route-shape validation
- draft status gate
- submit status transition (`Draft -> Queued`)
- request creation during submit
- plugin-driven job creation trigger via request status
- processor extracted to separate file/folder
- action enum (`BulkRequestAction`)

## What is next

To make the processor fully functional end-to-end:

1. Implement real `SaveItems` writes
- create new item rows
- update existing item rows
- no inferred deletes
- run/record item validation status

2. Recalculate and persist parent counters
- total, valid, invalid, duplicate, processed, failed

3. Implement `SubmitBatch` eligibility checks
- at least one item exists
- at least one valid item exists
- return clear validation errors when not eligible

4. Wire CSV parsing logic in file mode
- read file column
- parse rows
- stage rows as batch items

## Quick mental model

Think of `BulkDataRequestProcessor` as:

- API contract guard
- status gatekeeper
- flow orchestrator

Think of `DataverseBulkItemWriter` as:

- bulk persistence utility

This separation keeps business flow clear and prevents trigger classes from becoming hard to change.
