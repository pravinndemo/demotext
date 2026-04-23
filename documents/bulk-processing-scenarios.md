# Bulk Processing Scenarios (Agreed Model)

This document captures the agreed scenario-by-scenario behavior for Bulk Ingestion.

## Status Model

Batch statuses:

- `Draft`
- `Queued`
- `Processing`
- `Completed`
- `Failed`

Item statuses:

- `Pending`
- `Valid`
- `Invalid`
- `Duplicate`
- `Processed`
- `Failed`

Rules already agreed:

- Parent batch is created by user and starts in `Draft`.
- Removed items are deleted explicitly by the user on the form/subgrid.
- `SaveItems` does not infer deletions from missing items.
- `voa_canreprocess` controls retry eligibility for failed item rows.

## Endpoint Mapping (Implementation)

| Intent | Route | Caller pattern | Notes |
|---|---|---|---|
| Save items in Draft | `POST /bulk-data/save-items` | PCF selection save or CSV save via Custom API | Keeps batch in `Draft` |
| Final submit to queue and create work | `POST /bulk-data/submit-batch` | Final manual submit via Custom API | Moves `Draft -> Queued`, creates requests for valid items, and creates incidents directly when applicable |
| SVT single | `POST /bulk-data/svt-single` | SVT caller via Custom API | Bypasses bulk batch item flow |

These are the only supported routes for this first-time implementation.

## Channel-Specific Button Placement (Final)

- PCF selection channel: `Save Items` button must be inside the PCF.
- CSV upload channel: `Save Items` button can be a custom form button.

Rationale:

- PCF selection values are held in PCF state until explicitly saved, so they must be sent from inside PCF.
- CSV upload is already persisted on the batch/form record, so a form button can trigger server-side parsing and item creation.

Unified behavior after placement differences:

- Both buttons call the same logical action: `SaveItems`.
- Both flows keep batch status as `Draft`.
- Both flows converge on final manual `SubmitBatch` (`Draft -> Queued`).

## Two-Submit Model (Explicit)

There are two separate submit actions in the user journey:

1. `SaveItems` submit (inside PCF or CSV flow)
2. Final `SubmitBatch` submit (manual user confirmation)

The first submit creates or updates batch items only. It does not queue processing.
The second submit performs final validation, creates requests for valid items, and moves the batch to `Queued`.

This two-step behavior applies to both channels:

- PCF selection channel
- CSV upload channel

### Channel A: PCF selection

- User selects hereditaments in PCF and clicks submit in that PCF step.
- Custom API calls HTTP function to create or upsert `Bulk Ingestion Item` rows only.
- Batch remains `Draft`.
- User reviews item rows and statuses.
- User clicks final submit to queue batch.

### Channel B: CSV upload

- User uploads CSV and clicks submit in that CSV step.
- Custom API calls HTTP function to parse and create or upsert `Bulk Ingestion Item` rows only.
- Batch remains `Draft`.
- User reviews item rows and statuses.
- User clicks final submit to queue batch.

## Scenario 1: Create Batch Header

Preconditions:

- User creates a new batch in UI/PCF.

Action:

- User saves the batch header.

Expected outcome:

- Batch is created with status `Draft`.
- No item rows exist yet.
- Counters are initialized to zero.

## Scenario 2: Add First Set of Items (SaveItems)

Preconditions:

- Batch exists and status is `Draft`.

Action:

- User selects 10 SSUs (or uploads file rows) and invokes `SaveItems`.

Validation:

- Batch exists.
- Batch status is `Draft`.
- Payload is valid and non-empty.
- SSU-level validation and duplicate checks run.

Expected outcome:

- Item rows are created or upserted.
- Item statuses become `Valid`/`Invalid`/`Duplicate`.
- Batch counters are recalculated.
- Batch remains `Draft`.

## Scenario 3: Add More Items Later (Repeated SaveItems)

Preconditions:

- Same batch is still `Draft`.

Action:

- User returns later and adds 50 more items, then saves.

Validation:

- Same as `SaveItems` validation.
- Upsert key is `(batchId, ssuId)` to avoid duplicate rows.

Expected outcome:

- Existing rows are preserved.
- New/updated rows are merged correctly.
- Counters are recalculated from current rows.
- Batch remains `Draft`.

## Scenario 4: Remove Items in Draft

Preconditions:

- Batch is `Draft`.
- Batch has existing item rows.

Action:

- User removes selected items directly on the form/subgrid.

Expected outcome:

- Removed rows are deleted explicitly from `Bulk Ingestion Item` by the form-side action.
- Counters are recalculated immediately.
- Batch remains `Draft`.

## Scenario 5: SubmitBatch with Validation Failure

Preconditions:

- Batch is `Draft`.

Action:

- User triggers final `SubmitBatch`.

Validation:

- Batch exists and is `Draft`.
- At least one item exists.
- At least one item is `Valid`.

Expected outcome when validation fails:

- HTTP returns validation error.
- Batch remains `Draft`.
- User can fix items and submit again.

## Scenario 6: SubmitBatch Success

Preconditions:

- Batch is `Draft`.
- Submit validation passes.

Action:

- User triggers final `SubmitBatch`.

Expected outcome:

- Batch transitions `Draft -> Queued`.
- Submission metadata is stamped.
- Requests are created for valid items.
- If template mode is `Request and Job(s)`, the Azure Function creates the incident directly and links it back to the request and bulk item.
- Batch becomes read-only for normal edits.

## Scenario 7: Optional Worker Processing

Preconditions:

- Batch is `Queued`.

Action:

- Worker follow-up applies only when immediate submit-time creation is disabled or additional queued handling is required.

Expected outcome:

- Batch transitions `Queued -> Processing`.
- Each eligible item transitions to `Processed` or `Failed`.
- Batch completes as:
  - `Completed` when all eligible items succeed.
  - `Failed` when processing outcome is failure.

## Scenario 8: Reprocess Failed Items

Preconditions:

- Item rows exist in `Failed`.

Action:

- Retry logic picks failed items.

Retry selection criteria:

- `validationstatus = Failed`
- `voa_canreprocess = Yes`
- `voa_lockedforprocessing = No`

Expected outcome:

- Only retry-eligible failed rows are retried.
- On successful retry:
  - Item status becomes `Processed`.
  - `voa_canreprocess` is set to `No`.

## Field Notes: `voa_canreprocess`

- `Invalid` -> `No`
- `Duplicate` -> `No`
- `Failed` (processing-time failure) -> `Yes`
- `Processed` -> `No`

`voa_canreprocess` is system-managed and should not be user-editable.
