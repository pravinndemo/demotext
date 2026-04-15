# Bulk Processor - API and Azure Function Recommendation

I want the server-side shape to stay simple, predictable, and easy to support.

My preferred flow is:

**PCF -> Custom API -> Plugin logic -> SharePoint upload -> Dataverse update**

For orchestration, I want **Azure Function** rather than Power Automate. Azure Function is a better long-term fit for reliability, retry control, chunked processing, and monitoring.

---

## 1. Architecture Decision

### What I want

- PCF stays thin and only handles user interaction.
- Custom API is the entry point from the UI.
- Plugins perform the server-side work where transaction safety matters.
- Dataverse remains the system of record.
- Azure Function owns orchestration and background processing.

### What I do not want

- multiple direct calls from PCF to separate backend steps
- upload and metadata update split into different APIs
- orchestration logic embedded in the UI
- Power Automate as the primary processing engine

---

## 2. Server-Side Responsibilities

### PCF

The PCF should handle:

- searching and selecting records
- displaying counts and user feedback
- sending a single request to the backend

The PCF should not handle:

- file parsing
- bulk Request / Job creation
- long-running processing
- authoritative validation

### Custom API

The Custom API should expose reusable business operations and keep the UI integration clean.

### Plugins

Plugins should handle:

- synchronous validation
- record locking
- status enforcement
- SharePoint upload
- Dataverse updates that must happen as part of the same operation

### Azure Function

Azure Function should handle:

- reading submitted batches
- parsing staged CSV files
- calling Custom APIs
- chunking and retries
- batch completion updates
- processing summaries

---

## 3. API Design

I want to keep this to **three APIs only**.

## 3.1 `voa_UploadBulkProcessorFile`

This is the main upload API.

### Purpose

- accept the file from PCF
- validate the file and batch context
- upload the file into the correct SharePoint folder
- update the Bulk Processor with file metadata
- return the upload result

### Why this should stay as one API

I do not want upload and Bulk Processor update split into separate APIs.

If they are split, I create a failure gap where:

1. upload succeeds
2. Bulk Processor update fails

That creates inconsistency and extra support overhead.

### Suggested input

```json
{
  "bulkProcessorId": "BP-20260415-003",
  "fileName": "london-intake.csv",
  "contentType": "text/csv",
  "fileContentBase64": "..."
}
```

### Plugin responsibilities

1. Validate that the batch exists.
2. Validate that the batch is eligible for upload.
3. Validate that `Source Type = CSV`.
4. Validate that the file extension is `.csv`.
5. Build the SharePoint folder path for the batch.
6. Upload the file to SharePoint.
7. Update Bulk Processor fields:
   - `voa_filereference`
   - `voa_filesharepointid`
   - `voa_fileoriginalname`
   - `voa_fileuploadedon`
   - `voa_fileuploadedby`
8. Return the result.

### Suggested output

```json
{
  "success": true,
  "bulkProcessorId": "BP-20260415-003",
  "fileReference": "sharepoint://bulk/BP-20260415-003/london-intake.csv",
  "sharePointFileId": "SP-998877",
  "fileName": "london-intake.csv",
  "message": "File uploaded successfully"
}
```

---

## 3.2 `voa_CreateBulkProcessorItems`

This API handles line staging and should stay separate from file upload.

### Purpose

- create Bulk Processor Item records
- apply staging validation
- mark rows as Valid, Invalid, or Duplicate
- update Bulk Processor summary counts

### Suggested input

```json
{
  "bulkProcessorId": "BP-20260415-003",
  "sourceType": "PCF",
  "items": [
    {
      "sourceValue": "SSU-200101",
      "ssuId": "SSU-200101",
      "sourceRowNumber": null
    },
    {
      "sourceValue": "SSU-200102",
      "ssuId": "SSU-200102",
      "sourceRowNumber": null
    }
  ]
}
```

### Plugin responsibilities

1. Validate the batch.
2. Validate item values.
3. Create Bulk Processor Item rows.
4. Set item status:
   - Pending
   - Valid
   - Invalid
   - Duplicate
5. Update batch counts.
6. Optionally move the batch to `Items Created`.

---

## 3.3 `voa_CreateRequestAndJobFromBulkProcessorItem`

This is the processing API.

### Purpose

- pick one staged item
- validate business rules
- create Request
- create Job
- link the two records
- update item status and result

### Suggested input

```json
{
  "bulkProcessorItemId": "BPI-B-005",
  "processingRunId": "RUN-20260415-02"
}
```

### Plugin responsibilities

1. Validate that the item is eligible.
2. Resolve assignment.
3. Create Request.
4. Create Job.
5. Link the records.
6. Update item status.
7. Return success or failure.

### Suggested failure output

```json
{
  "bulkProcessorItemId": "BPI-B-005",
  "success": false,
  "requestId": null,
  "jobId": null,
  "status": "Failed",
  "message": "Request creation failed due to missing mandatory ownership mapping"
}
```

### Suggested success output

```json
{
  "bulkProcessorItemId": "BPI-A-001",
  "success": true,
  "requestId": "REQ-60001",
  "jobId": "JOB-70001",
  "status": "Processed",
  "message": "Request and Job created successfully"
}
```

---

## 4. SharePoint Storage Design

I want folder-based storage for each batch.

### Recommended structure

```text
/BulkProcessorUploads/{BulkProcessorId}/{OriginalFileName}
```

### Example

```text
/BulkProcessorUploads/BP-20260415-003/london-intake.csv
```

### Why this works

- one folder per batch
- no file name collision
- easy support lookup
- easier archival later

### Optional safer filename pattern

```text
/BulkProcessorUploads/BP-20260415-003/20260415_213012_london-intake.csv
```

I would still keep the original file name in Dataverse.

---

## 5. File Upload Constraint

I want to be explicit about one practical constraint.

### Large payloads are not ideal through Custom API

If the PCF sends file content as base64 or a large string payload to Dataverse plugin logic, then:

- payload size becomes a concern
- large files become harder to support
- debugging becomes harder

### My MVP assumption

This approach is acceptable for:

- CSV only
- controlled small or medium files
- limited MVP batch sizes

### My preferred MVP guardrails

- 1 MB or 2 MB max file size
- fixed CSV template only
- one upload per batch
- upload allowed only while batch is Draft

If I need large-file support later, I would move file handling to a dedicated Azure endpoint instead of pushing large base64 payloads through Custom API.

---

## 6. Azure Function Orchestration

I want Azure Function to own orchestration instead of Power Automate.

### Why Azure Function

I want:

- better reliability
- better retry control
- better support for chunked processing
- better handling of larger volumes
- cleaner long-term maintenance

### Azure Function responsibilities

- read submitted batches
- process only eligible items
- handle item locking
- read CSV files from SharePoint
- call `voa_CreateBulkProcessorItems`
- call `voa_CreateRequestAndJobFromBulkProcessorItem`
- update summary counts
- move the batch to `Completed`, `Partially Failed`, or `Failed`

### Azure Function should not

- duplicate business rules already enforced by plugins or Custom API
- become a second copy of validation logic
- scatter processing behaviour across multiple places

---

## 7. What the PCF Should Call

### CSV route

1. Call `voa_UploadBulkProcessorFile`.
2. Show the upload result.
3. Let Azure Function handle parsing and item staging.

### PCF selection route

1. Call `voa_CreateBulkProcessorItems`.

### Processing route

The PCF should not call processing directly.

Azure Function should call:

1. `voa_CreateRequestAndJobFromBulkProcessorItem`

---

## 8. When Rows Are Created

I want parsing to stay separate from upload.

### Preferred flow

1. `voa_UploadBulkProcessorFile`
2. Azure Function reads the SharePoint file
3. Azure Function calls `voa_CreateBulkProcessorItems`
4. User reviews staged rows
5. User submits the batch
6. Azure Function calls `voa_CreateRequestAndJobFromBulkProcessorItem`

### Why I prefer this

- cleaner responsibilities
- easier debugging
- easier retry
- easier support

---

## 9. Final Recommendation

My final recommendation is:

- use **3 APIs only**
- keep upload and metadata update inside `voa_UploadBulkProcessorFile`
- keep line staging inside `voa_CreateBulkProcessorItems`
- keep final Request and Job creation inside `voa_CreateRequestAndJobFromBulkProcessorItem`
- use **Azure Function** for orchestration
- do not use Power Automate as the primary engine

That gives me a design that is tight enough for MVP, but still scalable later.

---

## 10. Recommended Summary Wording

I propose that the PCF calls a dedicated upload Custom API, which handles the SharePoint folder upload and updates the Bulk Processor record with the file metadata in the same server-side operation. I would then keep Bulk Processor Item creation as a separate Custom API, and final Request and Job creation as another separate Custom API. This gives me a clean split between file upload, line staging, and final processing.

Azure Function should then handle orchestration, batch pickup, retries, and summary updates rather than Power Automate.
