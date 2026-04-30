# HTTP Trigger Flow for Bulk + SVT

I am documenting this so we all have one clear and practical flow before we continue with implementation.

This page explains how I want the HTTP-triggered Azure Function to behave for all three entry paths:

- Bulk selection from Hereditament PCF
- Bulk file upload from Hereditament PCF
- SVT single-item request from SVT PCF (outside bulk) with a separate SVT tracking row

## Why I am doing this

I want explicit endpoints for each intent so callers do not depend on action strings in payload.
I still keep shared routing logic based on payload shape within the function implementation.

At the same time, I want strict separation in behavior:

- Bulk = staging plus submit-driven request creation
- SVT = direct single request/job creation flow driven by a dedicated tracking row and status polling

## Components in the call chain

For all three paths, the high-level call chain remains:

1. PCF invokes Dataverse Custom API
2. Custom API invokes APIM endpoint
3. APIM calls HTTP-triggered Azure Function
4. Function validates and routes by contract mode

After that, behavior depends on the mode.

## Bulk Ingestion Lifecycle

The user journey for bulk processing is:

1. User saves Bulk Ingestion header with `status = Draft`
2. User selects hereditaments via PCF OR uploads CSV file and can save items multiple times while still in Draft
3. User clicks final "Submit Batch" on PCF
4. Custom API calls HTTP Azure Function
5. HTTP function validates the batch is still `Draft`, validates staged items, creates requests for valid rows, and moves batch to `Queued`
6. For `Request and Job(s)`, the Azure Function creates jobs directly and updates the request with a bypassed follow-up update to avoid duplicate plugin firing

## Minimal Status Names (Batch)

I will keep only these batch statuses:

- `Draft`: Header and items are still editable
- `Queued`: Handoff accepted and waiting for worker
- `Processing`: Worker is running
- `Completed`: Processing finished successfully
- `Failed`: Processing failed

## Final Agreed Rules (Single Source)

This section is the final agreed behavior for bulk batches.

1. Parent batch is created by user and starts as `Draft`.
2. HTTP function creates and updates only `Bulk Ingestion Item` rows.
3. User can add items repeatedly while the batch is `Draft` (for example 10 now, then 50, then another 50).
4. `SaveItems` keeps the batch status as `Draft`.
5. `SubmitBatch` moves the batch from `Draft` to `Queued` when validation passes.
6. Timer picks only `Queued` batches.
7. There is no `Ready to Submit` state in this simplified model.

### HTTP actions

- `SaveItems`: Create or upsert item rows while batch is Draft.
- `SubmitBatch`: Final checks and status transition Draft -> Queued.

### Endpoint mapping (current implementation)

| Intent | Route | Typical caller | Payload shape | `Action` field required |
|---|---|---|---|---|
| Save batch items | `POST /bulk-data/save-items` | PCF selection step or CSV save button via Custom API/APIM | Bulk payload (`bulkProcessorId` + selection ids or file context) | No |
| Final batch submit | `POST /bulk-data/submit-batch` | Final manual submit via Custom API/APIM | Bulk payload (`bulkProcessorId` with staged items already present) | No |
| SVT single processing | `POST /bulk-data/svt-single` | SVT caller via Custom API/APIM | SVT payload (`ssuid` + `userId` + `componentName`) | No |

These are the only supported routes for this first-time implementation.

### Two submit actions (important)

There are two submit actions and they are not the same:

1. First submit from PCF selection step or CSV step calls Custom API to create/update batch items only.
2. Final manual submit calls Custom API to validate and move batch from `Draft` to `Queued`.

This applies equally to:

- PCF selection flow
- CSV upload flow

### SaveItems validation

- `bulkProcessorId` required and must exist.
- Batch status must be `Draft`.
- Input must be valid selection payload (`ssuIds[]`) or valid file payload.
- Reject empty item list or invalid file.
- In `Draft`, users can add items and remove items at any time.
- Validate each SSU and detect duplicates.
- Upsert items by `(batchId, ssuId)` to avoid duplicate rows across repeated saves.
- `SaveItems` creates or updates rows only. It does not infer deletions.
- If a user removes an item in `Draft`, that delete happens explicitly on the form/subgrid side.
- Recalculate item counters and keep parent status as `Draft`.

### SubmitBatch validation

- `bulkProcessorId` required and must exist.
- Batch status must be `Draft`.
- At least one item must exist.
- At least one `Valid` item must exist.
- On success move status `Draft -> Queued` and stamp submission metadata.
- `SubmitBatch` is the final manual action after user reviews staged item statuses.

### `voa_canreprocess` explicit behavior

- `voa_canreprocess` is an item-level retry flag managed by the system.
- Set `voa_canreprocess = No` for `Invalid` and `Duplicate` rows.
- Set `voa_canreprocess = Yes` for processing-time `Failed` rows.
- Set `voa_canreprocess = No` once a row becomes `Processed`.
- Timer retry queries must include:
	- `validationstatus = Failed`
	- `voa_canreprocess = Yes`
	- `voa_lockedforprocessing = No`

## Contract modes

### 1) Bulk selection mode

Request carries:

- `bulkProcessorId`
- `ssuIds[]`

Expected behavior:

- **Gate check: Batch must be in `Draft` status. Reject if not.**
- Validate parent batch and payload
- Stage `Bulk Ingestion Item` records with `validationStatus = Pending`
- Validate each item, update status to `Valid`/`Invalid`/`Duplicate`
- Update `Bulk Ingestion` counters (validItemCount, invalidItemCount, duplicateItemCount, totalRows)
- **For `SaveItems`: keep status as `Draft`**
- **For `SubmitBatch`: move `Draft -> Queued`**
- Return accepted response

Important:

- This path must not create jobs directly in the HTTP trigger
- Request and job creation can both happen in `SubmitBatch`

### 2) Bulk file mode

Request carries:

- `bulkProcessorId`

Expected behavior:

- **Gate check: Batch must be in `Draft` status. Reject if not.**
- Resolve source file for the batch from Dataverse file column
- Parse and validate rows
- Stage `Bulk Ingestion Item` records with `validationStatus = Pending`
- Validate each item, update status to `Valid`/`Invalid`/`Duplicate`
- Update `Bulk Ingestion` counters and store file reference
- **For `SaveItems`: keep status as `Draft`**
- **For `SubmitBatch`: move `Draft -> Queued`**
- Return accepted response

Important:

- Same as selection for staging
- Submit processing creates requests and, for `Request and Job(s)`, creates the incident directly in the Azure Function

### 3) SVT single mode

Request carries:

- `ssuid`
- `userId`
- `componentName`

Expected behavior:

- Validate single SSUID and caller context
- Use the dedicated SVT tracking row, not the bulk tables
- Set the tracking row to `Queued` / `Requested`
- Trigger direct request creation path for one item
- Persist `componentName` into request metadata for traceability
- Link the created request and incident directly from the Azure Function
- Update the tracking row with `requestId`, `jobId`, and final status
- Return accepted/direct response for single processing

## Routing matrix

I will use this exact matrix:

- `bulkProcessorId + ssuIds[]` => `BULK_SELECTION`
- `bulkProcessorId` only => `BULK_FILE`
- `ssuid + userId + componentName` => `SVT_SINGLE`
- Anything else => `400 Bad Request`

## Response behavior

Common response structure should include:

- `accepted`
- `code`
- `message`
- `routeMode`
- `correlationId`

Bulk-specific response can include:

- `bulkProcessorId`
- `receivedCount`
- `stagingStatus`

SVT-specific response can include:

- `ssuid`
- `userId`
- `componentName`
- direct processing status indicator

## Validation rules I want

1. Reject mixed contracts (for example, both `bulkProcessorId` and `ssuid`).
2. Reject partial SVT payloads (for example, `ssuid` without `userId`).
3. Reject empty arrays for bulk selection.
4. Always return actionable error messages.
5. Always pass and log `correlationId` for traceability across Dataverse, APIM, and Function logs.

## Processing ownership

I want to keep these boundaries stable:

- HTTP trigger owns contract validation and route selection
- Bulk staging remains in HTTP flow
- Bulk final request creation occurs in `SubmitBatch`
- Bulk job creation uses direct incident creation inside the Azure Function
- SVT uses a separate tracking row, async plug-in dispatch, and status polling from PCF

## First implementation step

For first code change, I will do contract-first updates in the HTTP trigger:

- request model includes SVT fields
- response model includes route and SVT metadata fields
- route-decision logic supports all three modes and rejects invalid combinations

After contract-first change is in place, I will wire SVT direct execution path in the next step without disturbing the bulk timer behavior.
