# Bulk Processor End-to-End Flow

Team,

I put together the end-to-end flow for the bulk processor journey so we have one clear picture of how the process should work from start to finish.

In this flow:

- `Bulk Processor` is the parent record.
- `Bulk Processor Item` is the staged child row.
- Azure Function creates or updates the batch, batch items, and request records.
- Azure Function creates request and incident records directly for `Request and Job(s)` mode.

## Flow Summary

1. The admin user opens the `Bulk Processor` entity form in the model-driven app.
2. The admin fills in the `Bulk Processor` details, including the job context, assignment details, and input mode.
3. The admin uses the PCF to either:
   - select SSU items, or
   - upload a CSV file
4. The PCF sends one request to the Dataverse Custom API for the selected path.
5. The Custom API validates the request and forwards it to Azure Function.
6. Azure Function processes the request, reads the CSV from the Dataverse file column for the CSV path, and creates or updates `Bulk Processor` and `Bulk Processor Item` records in Dataverse in chunks.
7. The user reviews the staged `Bulk Processor Item` rows and submits the `Bulk Processor` when ready.
8. The submit path validates the staged items and creates `Request` records for valid rows.
9. If template `Case Work Mode = Request and Job(s)`, the function creates the request, creates the `Job` directly, and performs a bypassed follow-up update on the request so the existing plugin does not create a duplicate incident. If mode = `Request Only`, the function creates the request in `On Hold`, so no job is created.
10. The function updates the related `Bulk Processor` and `Bulk Processor Item` records in Dataverse and the batch is then completed, partial success, or failed depending on the processing outcome.

## Request Contracts

### 1. PCF selection request

When the user selects items in the PCF, the PCF sends only the selected identifiers plus the parent `Bulk Processor` id.

```json
{
  "bulkProcessorId": "7f3c2f6a-8d1c-4d5a-9c8b-123456789abc",
  "sourceType": "PCF",
  "ssuIds": [
    "SSU001",
    "SSU002",
    "SSU003"
  ]
}
```

This is the request shape for the selection path.

### 2. CSV upload request

When the user uploads a file in the PCF, the file is already stored on the Dataverse batch record and the downstream call only needs the parent `Bulk Processor` id.

```json
{
  "bulkProcessorId": "7f3c2f6a-8d1c-4d5a-9c8b-123456789abc",
  "sourceType": "CSV_DATAVERSE_FILE",
  "fileColumnName": "sourcefile"
}
```

This is the request shape for the file upload path.

## User Journey

### 1. Open the `Bulk Processor`
- The admin user opens the `Bulk Processor` form in the model-driven app.
- The form acts as the entry point for the whole process.

### 2. Fill `Bulk Processor` details
- The admin enters the `Bulk Processor` name and other required fields.
- The admin selects the requested job type.
- The admin selects the assignment mode and the assigned team or manager as needed.
- The admin chooses the input mode for the `Bulk Processor`.

### 3. Add items

#### PCF selection path
- The user searches and selects the relevant SSU items in the PCF.
- The PCF sends the `bulkProcessorId` plus the `ssuIds` array to the Custom API.
- The Custom API forwards the request to Azure Function.
- Azure Function validates the selection and creates `Bulk Processor Item` rows in Dataverse in chunks.

#### CSV upload path
- The user uploads the CSV file through the PCF.
- The PCF saves the file to the Dataverse file column on the batch.
- The Custom API forwards the request to Azure Function.
- Azure Function reads the file from the Dataverse file column, parses it, and creates `Bulk Processor Item` rows in Dataverse in chunks.

### 4. Staging and validation
- The selected or uploaded data is staged into `Bulk Processor Item`.
- Azure Function performs the validations and writes the results back to Dataverse.
- The parent `Bulk Processor` is updated with counts, validation status, file reference where applicable, and current status.

### 5. Review and submit
- The user reviews the staged `Bulk Processor Item` rows.
- The user submits the `Bulk Processor` when it is ready for processing.
- The `Bulk Processor` becomes eligible for background processing.

### 6. Submit processing and optional queued worker
- The `SubmitBatch` path validates the staged `Bulk Processor Item` rows for processing.
- The function creates the `Request` records.
- For `Request and Job(s)`, the Azure Function creates the request and incident directly.
- For `Request Only`, the request is created without creating an incident.
- The function updates the `Bulk Processor Item` row with the result.
- The function rolls the counts and status back up to the parent `Bulk Processor`.
- If `BulkSubmitCreateImmediately=false`, the same batch can remain `Queued` for a worker-style follow-up path.

### 7. Completion
- If all items succeed, the `Bulk Processor` completes successfully.
- If some items fail, the `Bulk Processor` is marked as partial success.
- If processing cannot continue, the `Bulk Processor` is marked as failed.

## Key Notes

- We should keep the upload path and the background processing path separate.
- We should not create `Request` or `Job` directly from the PCF in bulk.
- The Azure Function should own staging and request creation work.
- Job creation should happen directly in the Azure Function for the bulk flow, with follow-up request updates using bypass to avoid duplicate plugin execution.
- `Bulk Processor` should remain the parent record.
- `Bulk Processor Item` should remain the staged child record.
- All Dataverse writes should go through Azure Function.
- For PCF selection, we can safely send 100 to 1,000 SSU ids as a thin JSON payload, as long as we avoid sending full row objects.

## Short Version

The user opens the `Bulk Processor` form, fills in the details, and uses the PCF to either select SSU items or upload a CSV file. For selection, the PCF sends `bulkProcessorId + ssuIds` to the Custom API, which forwards it to Azure Function. For CSV upload, the file is stored in Dataverse and the downstream call sends `bulkProcessorId` plus file context to Azure Function. Azure Function stages `Bulk Processor Item` rows in Dataverse, the user submits the `Bulk Processor`, the function creates the `Request` records, and for `Request and Job(s)` mode it also creates the `Job` records directly in the function.
