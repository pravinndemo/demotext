# Bulk Processor End-to-End Flow

Team,

I put together the end-to-end user flow for the bulk processor journey so we have one clear picture of how the process should work from start to finish.

## Flow Summary

1. The admin user opens the `Bulk Processor` entity form in the model-driven app.
2. The admin fills in the batch details, including the job context, assignment details, and input mode.
3. The admin uses the PCF to either:
   - select records from the UI, or
   - upload a CSV file
4. The batch is then staged as a `Bulk Processor` record with one related `Bulk Processor Item` row per selected or uploaded item.
5. The user reviews the batch and submits it when ready.
6. The batch stays in Dataverse with status and counts updated as it moves through validation and staging.
7. A nightly Azure timer-triggered function picks up submitted batches.
8. The Azure Function validates the staged items, creates the `Request` and `Job` records, associates the job to the `Team`, and updates the related `Bulk Processor` and `Bulk Processor Item` records.
9. The batch is completed, partially completed, or failed depending on the processing outcome.

## User Journey

### 1. Open the batch
- The admin user opens the `Bulk Processor` form in the model-driven app.
- The form acts as the entry point for the whole process.

### 2. Fill batch details
- The admin enters the batch name and other required fields.
- The admin selects the requested job type.
- The admin selects the assignment mode and the assigned team or manager as needed.
- The admin chooses the input mode for the batch.

### 3. Add items
- If the user chooses the PCF selection path, they search and select the relevant records in the UI.
- If the user chooses the CSV path, they upload the file through the PCF.
- The PCF triggers the agreed Dataverse / backend flow to stage the batch data.

### 4. Staging and validation
- The selected or uploaded data is staged into `Bulk Processor Item`.
- Basic validation is performed on the staged data.
- The parent `Bulk Processor` is updated with file reference, item counts, validation counts, and status.

### 5. Review and submit
- The user reviews the staged batch.
- The user submits the batch when it is ready for processing.
- The batch becomes eligible for background processing.

### 6. Nightly processing
- A nightly Azure timer-triggered function runs.
- The function picks up eligible submitted batches.
- It reads the file or staged rows and performs the processing checks.
- It creates the `Request` and `Job` records.
- It associates the `Job` to the correct `Team`.
- It updates the `Bulk Processor Item` row with the result.
- It rolls the counts and status back up to the parent `Bulk Processor`.

### 7. Completion
- If all items succeed, the batch completes successfully.
- If some items fail, the batch is marked as partially failed.
- If processing cannot continue, the batch is marked as failed.

## Key Notes

- We should keep the upload path and the background processing path separate.
- We should not create `Request` or `Job` directly from the PCF in bulk.
- The nightly Azure Function should own the long-running processing work.
- `Bulk Processor` should remain the batch header.
- `Bulk Processor Item` should remain the staged child record.
- The status and counts on the parent record should reflect the latest batch outcome.

## Short Version

The user opens the `Bulk Processor` form, fills in the batch details, and uses the PCF to either select items or upload a file. The data is staged in Dataverse, the user submits the batch, and a nightly Azure timer-triggered function creates the `Request` and `Job` records, updates the team assignment, and writes the processing result back to `Bulk Processor` and `Bulk Processor Item`.
