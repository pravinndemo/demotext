# Bulk Processor End-to-End Business Flow

This document explains the bulk processing journey in business terms, from the user preparing a batch through to final processing by the timer.

## 1. What a Bulk Processor Is

A Bulk Processor is the parent batch record.

It holds:
- the batch reference
- the submission and processing status
- the selected job type
- the item counts
- the processing timestamps

The system generates `voa_batchreference`, then copies that value into `voa_name` so the primary name column and the business reference show the same batch number.

Each Bulk Processor Item is one row in that batch.

## 2. The Main Business Stages

The process has three main stages:

1. Draft and staging
2. Submit to queue
3. Background processing by the timer

The user starts the process in the app. The system finishes the work in the background.

## 3. Status Fields You Will See

There are two different status areas on the batch record.

### 3.1 Batch lifecycle status (`statuscode`)

This is the main business status for the batch.

| Status | Meaning |
|---|---|
| Draft | The batch is being prepared. Items can still be added, updated, or checked. |
| Queued | The batch has been submitted and is waiting for background processing. |
| Partial Success | Some items processed successfully, but at least one item still needs attention or can be retried. |
| Completed | All valid work finished successfully. |
| Failed | The batch did not complete successfully. |
| Cancelled | The batch was stopped by the user or the business process. |

### 3.2 Processing state (`voa_processingstatus`)

This is the technical processing state used during the workflow.

| Status | Meaning |
|---|---|
| Processing | The system is actively working on the batch. |
| Processed | The system finished the current step successfully. |
| Failed | The system hit an error while processing. |

### 3.3 Item validation and processing status (`voa_validationstatus`)

Each Bulk Processor Item has its own status.

| Status | Meaning |
|---|---|
| Pending | The item has been staged but not yet validated or processed. |
| Valid | The item passed staging validation and is ready to be processed. |
| Invalid | The item failed a validation rule. |
| Duplicate | The item duplicates another item in the same batch or another batch. |
| Processed | The item was processed successfully. |
| Failed | The item failed during processing. |

## 4. End-to-End Flow

### Step 1. User prepares the batch

The user opens the Bulk Processor form and fills in the batch details.

Typical setup includes:
- batch reference
- job type
- assignment details
- input method

At this stage the batch is in `Draft`.

### Step 2. User stages items

The user can stage items in one of two ways:

- select items in the UI
- upload a CSV file

The system creates or updates Bulk Processor Item rows and marks them as `Pending` while staging is in progress.

### Step 3. System validates the items

After staging, the system validates the batch items.

Typical checks include:
- SSU is present
- SSU is a valid GUID
- for CSV uploads, the file is a single SSU-ID column, so the SSU ID itself is the only value in each row
- duplicate SSU exists in the same batch
- duplicate SSU exists in another batch when that check is enabled

The cross-batch check compares SSU IDs on Bulk Processor Item rows across batches. It does not use `voa_name` for duplicate detection. The batch name is only used to show the user which other batch already contains that SSU.

Items are then marked as:
- `Valid` if they pass
- `Invalid` if they fail validation
- `Duplicate` if they are duplicates

The batch counters are refreshed after validation.

### Step 4. User submits the batch

When the user is ready, they submit the batch.

On submit:
- the batch moves from `Draft` to `Queued`
- the system records that the batch is waiting for background processing
- only valid items are eligible for the next stage

This submit action does not mean the business work is finished. It means the batch is now ready for the background worker.

### Step 5. Timer picks up the batch

The timer runs in the background.

It picks up batches that are:
- `Queued`
- `Partial Success`

The timer then:
- loads the valid items
- reads the SSU/hereditament value from each item row
- uses that value to create the request/job for the row
- uses that same value to check whether the same SSU already exists in another batch
- creates request records
- creates job records when that mode is enabled
- updates item statuses to `Processed` or `Failed`
- refreshes the batch counts
- sets the final batch status

### Step 5.1 Checks before request/job creation

The system checks these things before it creates a request or job:

1. The item must already be marked `Valid`.
1. The SSU value must be present on the item row.
1. The SSU value must be a valid GUID.
1. If cross-batch duplicate checking is enabled, the same SSU must not already exist on another batch item row.
1. The system must not already have an active request with the same SSU and job type.
1. The system must resolve a proposed billing authority for that SSU before the request is created.

Important:
- There is no separate committed SSU or proposed SSU state in the bulk flow.
- The word `proposed` in the code refers to proposed billing authority, not the SSU itself.
- If request/job creation is enabled, the request is created first and the job is created after that.

### Step 6. Final outcome

The final batch result is one of the following:

- `Completed` if all required work finished successfully
- `Partial Success` if some work succeeded but some items still need attention or retry
- `Failed` if the batch did not complete

## 5. Business Meaning of the Timer

The timer is the background worker.

From a business point of view, it is the step that turns a submitted batch into completed operational records.

It is important because:
- the user does not wait for every request or job to be created in the UI
- large batches can be processed safely in the background
- failures can be isolated to individual items without losing the whole batch

## 6. What The User Sees

In simple terms, the user journey is:

1. Create a batch in `Draft`
2. Add or upload items
3. Review validation results
4. Submit the batch
5. Wait for background processing
6. Check the final status:
   - `Completed`
   - `Partial Success`
   - `Failed`

## 7. Key Business Rules

- `Draft` means the batch is still editable.
- `Queued` means the batch has been handed off to background processing, but `voa_DelayProcessingUntil` can still be updated for support rescheduling.
- `Completed` means the batch is finished.
- `Partial Success` means the batch mostly worked, but not everything completed cleanly.
- `Failed` means the batch did not complete successfully.
- Item-level statuses explain what happened to each row.
- The batch reference is the business identifier used to trace the batch, and `voa_name` mirrors that value.
- CSV input is a one-column list of SSU IDs, so there is no separate business column beyond the SSU value itself.
- The timer reads the same SSU/hereditament value that was staged on the item earlier.
- The SSU value is the same identifier used for duplicate checks and request/job creation.

## 8. Simple Example

Example batch:
- 10 items uploaded
- 8 items pass validation
- 2 items fail validation

After submit:
- the batch becomes `Queued`

After timer processing:
- 8 items are `Processed`
- 2 items remain `Invalid` or `Failed`
- the batch is marked `Partial Success`

If all items complete successfully, the batch becomes `Completed`.

If nothing can be processed, the batch becomes `Failed`.

## 9. Business Discussion Script

Use this section as the business review note for the wiki or for a formal walkthrough.

### Review points

- The visible batch number comes from the automatically generated batch reference.
- The batch is editable while it is in `Draft`.
- Submitting the batch moves it to `Queued` and hands the work to the background timer.
- CSV input contains SSU IDs only.
- `Partial Success` means some items completed and at least one item still needs attention or retry.
- A batch with failed items remains reviewable until the business process is complete.

### Business explanation

The system creates the batch reference automatically and copies that value into the batch name so users see one consistent identifier. Users stage items while the batch is in `Draft`, submit it to move it to `Queued`, and the background timer completes the processing and sets the final outcome.

### Review check

- Confirm the labels `Draft`, `Queued`, `Completed`, `Partial Success`, and `Failed` match the business process.
- Confirm the batch name should show the same value as the batch reference.
- Confirm CSV upload remains limited to SSU IDs only.
- Confirm the `Partial Success` definition matches the expected business meaning.

## 10. Refinement Notes

After business review, refine this document by checking:

- whether the handoff step should use `submitted` or `queued`
- whether the business team prefers `batch number` or `batch reference`
- whether the timer should be described as `background processing` or `worker processing`
- whether `Partial Success` should stay as a rule or use an example
- whether any labels need to be simplified for business users

## 11. Validation Matrix

This is the full set of validations currently implemented in the bulk and SVT flows.
The SVT flow has its own validation checks before request or job creation.

### 11.1 Request and routing validation

| Check | What it means for the business | Code / result |
|---|---|---|
| Invalid JSON | The request payload cannot be read. | `INVALID_JSON` |
| Empty request | Nothing was sent in the request body. | `INVALID_REQUEST` |
| Wrong endpoint for payload | The payload shape does not match the endpoint. | `INVALID_ROUTE_FOR_ENDPOINT` |
| Missing bulk processor id | A bulk action was sent without a batch id. | `BULK_PROCESSOR_ID_REQUIRED` |
| Unsupported action | The bulk endpoint was called without a valid action. | `INVALID_ACTION` |
| Bulk header lookup failed | The system did not load the batch record. | `BULK_PROCESSOR_LOOKUP_FAILED` |
| Batch not in Draft | The user tried to stage or submit a batch that is no longer editable. | `BATCH_NOT_DRAFT` |

### 11.2 Submit validation

| Check | What it means for the business | Code / result |
|---|---|---|
| Template missing or format blank | The batch does not have the template data needed to submit. | `TEMPLATE_SOURCE_REQUIRED` |
| No items in batch | There are no batch items to process. | `NO_ITEMS_TO_SUBMIT` |
| No valid items | Every item failed validation, so nothing can be created. | `NO_VALID_ITEMS_TO_SUBMIT` |
| Job type missing | The batch cannot be mapped to the required request/job type. | `JOB_TYPE_REQUIRED` |
| Submit failed | An unexpected error happened during submit. | `SUBMIT_BATCH_FAILED` |
| Save items failed | An unexpected error happened while staging items. | `SAVE_ITEMS_FAILED` |

### 11.3 Item staging validation

| Check | What it means for the business | Code / result |
|---|---|---|
| SSU missing | The item has no SSU value. | `ERR_SSU_REQUIRED` |
| SSU is not a valid GUID | The item value is not in the expected identifier format. | `ERR_SSU_INVALID_GUID` |
| SSU value required when configured | A configured mandatory SSU value is missing for that item. In the CSV flow, this is the same SSU value that was staged. | `ERR_SOURCE_REQUIRED` |
| Duplicate SSU in same batch | The same SSU appears more than once in this batch. | `ERR_DUP_SSU_SAME_BATCH` |
| Duplicate SSU value in same batch | The same staged SSU value appears more than once in this batch when that check is enabled. | `ERR_DUP_SOURCE_SAME_BATCH` |
| Duplicate SSU in another batch | The same SSU already exists on another batch item row when that check is enabled. | `ERR_DUP_SSU_OTHER_BATCH` |

### 11.4 Request and job creation validation

| Check | What it means for the business | Code / result |
|---|---|---|
| Single-item call produced no result | The system did not produce a request/job outcome for the row. | `NO_RESULTS` |
| User id is not a GUID | The submit user value is invalid. | `INVALID_USER_FORMAT` |
| SSU is not a valid GUID at creation time | The item cannot be turned into a request/job because the SSU value is malformed. | `INVALID_SSU_FORMAT` |
| Active request already exists | There is already an active request for the same SSU and job type. | `ACTIVE_REQUEST_PRESENT` |
| Proposed billing authority not resolved | The system did not determine the billing authority context needed for request creation. | `ERROR_PROPOSED_SSU` |
| Request/job creation failed | An unexpected error happened while creating the request or job. | `CREATION_FAILED` |

### 11.5 SVT tracking validation

| Check | What it means for the business | Code / result |
|---|---|---|
| Missing SVT processing id | The SVT tracking request did not provide the required row id. | `SVT_PROCESSING_ID_REQUIRED` |
| SVT lookup failed | The system did not read the SVT tracking row. | `SVT_TRACKING_LOOKUP_FAILED` |
| SVT already processed | The SVT row is already completed. | `SVT_ALREADY_PROCESSED` |
| SVT already processing | The SVT row is already being worked on. | `SVT_ALREADY_PROCESSING` |
| Dispatch not requested | The SVT row has not been marked ready for processing. | `SVT_DISPATCH_NOT_REQUESTED` |
| Missing SVT required fields | The SVT row is missing `ssuid`, `userId`, or `componentName`. | `INVALID_SVT_REQUEST` |
| SVT request creation failed | The request was not created for the SVT row. | `SVT_REQUEST_FAILED` |
| SVT job creation failed | The job was not created for the SVT row. | `SVT_JOB_FAILED` |

### 11.6 Practical summary

- The bulk flow validates the batch header first, then the items, then the request/job creation steps.
- The timer uses the SSU value from each item row.
- The same SSU identifier is used throughout validation, duplicate checks, and request/job creation.
- The word `proposed` in the code refers to proposed billing authority, not the SSU itself.
