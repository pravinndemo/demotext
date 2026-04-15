# Bulk Processor - API and Azure Function Recommendation

I want the server-side shape to stay simple and predictable.

My preferred approach is:

**PCF -> Custom API -> Plugin logic -> SharePoint folder upload -> Dataverse update**

That is cleaner than letting the PCF make multiple direct calls.

I also want to use **Azure Function** for orchestration instead of Power Automate, because it is a better fit for reliability, scale, and retry control.

---

## 1. Recommended Server-Side Shape

### My recommendation

I want to use **Custom API as the entry point from PCF**, and keep the actual SharePoint upload and Dataverse updates inside plugin logic behind that API.

That means:

- the PCF sends one request
- the Custom API receives it
- the plugin performs the server-side work
- Dataverse is updated in the same controlled flow

This is cleaner than splitting the upload and update into multiple small APIs.

---

## 2. API Set

I want to keep this to **three APIs only**.

### API 1 - `UploadBulkProcessorFile`

This is the main upload API.

#### Purpose

- accept the file from the PCF
- validate it
- upload it into the correct SharePoint folder
- update the Bulk Processor with file metadata
- return the result

#### I do not want to split this into separate upload and update APIs

I would not create a separate `UpdateBulkProcessorAfterUpload` API for MVP.

If I split them, I create a risk where:

1. upload succeeds
2. Bulk Processor update fails

That creates inconsistency.

I want one API to own the upload transaction flow.

#### Suggested input

```json
{
  "bulkProcessorId": "BP-20260415-003",
  "fileName": "london-intake.csv",
  "contentType": "text/csv",
  "fileContentBase64": "..."
}
```

#### What the plugin should do

1. validate that the batch exists
2. validate that the batch status allows upload
3. validate that `Source Type = CSV`
4. validate that the file extension is `.csv`
5. build the SharePoint folder path for the batch
6. upload the file to SharePoint
7. update Bulk Processor fields:
   - `voa_filereference`
   - `voa_filesharepointid`
   - `voa_fileoriginalname`
   - `voa_fileuploadedon`
   - `voa_fileuploadedby`
8. return upload result

#### Suggested output

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

### API 2 - `CreateBulkProcessorItems`

This API is separate and should stay separate.

#### Purpose

- create Bulk Processor Item records
- apply staging validation
- mark rows as Valid, Invalid, or Duplicate
- update summary counts on Bulk Processor

This API is for **line staging**, not file upload.

#### Suggested input

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

#### What the plugin should do

1. validate the batch
2. validate item values
3. create Bulk Processor Item rows
4. mark status:
   - Pending
   - Valid
   - Invalid
   - Duplicate
5. update batch counts
6. optionally move batch to `Items Created`

---

### API 3 - `CreateRequestAndJobFromBulkProcessorItem`

This is the processing API.

#### Purpose

- pick one staged item
- validate business rules
- create Request
- create Job
- link them
- update item status and result

This must stay separate from upload and staging.

#### Suggested input

```json
{
  "bulkProcessorItemId": "BPI-B-005",
  "processingRunId": "RUN-20260415-02"
}
```

#### What the plugin should do

1. validate the item is eligible
2. resolve assignment
3. create Request
4. create Job
5. link the records
6. update item status
7. return success or failure

#### Suggested failure output

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

#### Suggested success output

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

## 3. SharePoint Folder Design

I want folder-wise storage.

### Recommended structure

```text
/BulkProcessorUploads/{BulkProcessorId}/{OriginalFileName}
```

### Example

```text
/BulkProcessorUploads/BP-20260415-003/london-intake.csv
```

That gives me:

- one folder per batch
- no file name collision
- easy support lookup
- easy archival later

### Optional safer filename pattern

```text
/BulkProcessorUploads/BP-20260415-003/20260415_213012_london-intake.csv
```

I would still keep the original file name in Dataverse.

---

## 4. Important Caution on File Upload Through Custom API

This approach is valid, but I need to keep one practical limitation in mind:

### Large file payloads are not ideal

If the PCF sends file content as base64 or string payload to Dataverse plugin logic, then:

- payload size matters
- large files become painful
- debugging becomes harder

So I think this is fine for:

- CSV only
- controlled small or medium files
- MVP batch sizes

### My MVP limit

I would set a sensible file limit for MVP, for example:

- 1 MB or 2 MB max
- fixed CSV template only

For larger files later, I would move upload handling to a dedicated Azure endpoint instead of pushing large base64 through Custom API.

---

## 5. Azure Function for Orchestration

I want **Azure Function** to be the orchestration layer rather than Power Automate.

### Why Azure Function

I want:

- better reliability
- better retry control
- better chunked processing
- better support for larger volumes
- cleaner long-term maintenance

### Azure Function responsibilities

- read submitted batches
- process only eligible items
- handle item locking
- call `CreateBulkProcessorItems` for parsed CSV rows
- call `CreateRequestAndJobFromBulkProcessorItem` for each staged item
- update summary counts
- move the batch to `Completed`, `Partially Failed`, or `Failed`

### Azure Function should not

- duplicate core business rules already handled by Custom API or plugins
- create a second copy of validation logic
- become the place where business rules are spread out and difficult to test

---

## 6. Recommended API Set

I want to keep the solution small and focused.

### API 1 - `voa_UploadBulkProcessorFile`

Purpose:

- upload CSV to SharePoint
- update Bulk Processor file metadata

### API 2 - `voa_CreateBulkProcessorItems`

Purpose:

- create staged child items
- apply staging validation

### API 3 - `voa_CreateRequestAndJobFromBulkProcessorItem`

Purpose:

- process one child item into Request and Job

That is enough for MVP.

---

## 7. What the PCF Should Call

### For the CSV route

1. call `voa_UploadBulkProcessorFile`
2. show the uploaded file result
3. let Azure Function or a follow-up server step handle parsing and item staging

### For the PCF selection route

1. call `voa_CreateBulkProcessorItems`

### For the processing route

Not PCF.

Azure Function should call:

1. `voa_CreateRequestAndJobFromBulkProcessorItem`

---

## 8. When Do Rows Get Created?

I still want to keep parsing separate from upload.

### My preferred flow

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

## 9. My Final Recommendation

Yes, I want to use plugin and Custom API, but I want the design to stay tight:

### Use 3 APIs only

- `voa_UploadBulkProcessorFile`
- `voa_CreateBulkProcessorItems`
- `voa_CreateRequestAndJobFromBulkProcessorItem`

### Do not create a separate update-after-upload API

That should happen inside `voa_UploadBulkProcessorFile`.

### Keep parsing separate from upload

That will make the solution much easier to manage.

### Use Azure Function instead of Power Automate

I want Azure Function to own orchestration because it is a better fit for reliability and growth.

---

## 10. Recommended Summary Wording

I propose that the PCF calls a dedicated upload Custom API, which handles the SharePoint folder upload and updates the Bulk Processor record with the file metadata in the same server-side operation. I would then keep Bulk Processor Item creation as a separate Custom API, and final Request and Job creation as another separate Custom API. This gives me a clean split between file upload, line staging, and final processing.

Azure Function should then handle orchestration, batch pickup, retries, and summary updates instead of Power Automate.
