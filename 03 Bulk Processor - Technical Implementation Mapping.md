# Bulk Processor - Technical Implementation Mapping

Based on the agreed data design, validation rules, and status transitions, I have mapped the solution into implementable technical responsibilities.

The purpose of this document is to make it clear:

- what belongs in the PCF
- what belongs in Dataverse plugins and Custom APIs
- what belongs in a backend integration service
- what belongs in Azure Function
- what should remain system-managed
- what API contracts are needed
- how I expect the end-to-end flow to behave

This should help the team move from design into delivery planning.

---

## 1. Implementation Principles

### Core implementation principles

1. I want the PCF to remain a thin UI layer.
2. Bulk Request and Job creation should not happen synchronously from the UI for large volumes.
3. Dataverse remains the system of record for batch and item states.
4. Custom APIs should hold reusable business operations.
5. A backend integration service should handle SharePoint upload work.
6. Azure Function should own orchestration and background processing.
7. Validation should remain split into:
   - staging validation
   - processing validation
8. The solution must support partial success.
9. The solution must support traceability at line level.

---

## 2. Architecture Direction

I do not want a single monolithic plugin or a single catch-all Custom API.

The processing model has different execution needs, so I am splitting the server-side logic into smaller, purpose-built components:

- batch validation and state transition plugins
- staging Custom APIs
- processing Custom APIs
- summary and locking helpers where needed
- Azure Function orchestration

This keeps the solution easier to test, easier to maintain, and safer to evolve.

### My preferred runtime split

| Layer | Primary Role | Notes |
|---|---|---|
| PCF | Search, selection, and staging initiation | Thin UI only |
| Model-driven app | Batch entry point and review surface | Host experience |
| Dataverse plugins | Synchronous validation and state control | Best for transaction-safe rules |
| Custom APIs | Reusable business actions | Three APIs only |
| Backend Integration Service | SharePoint upload and file transfer | Keeps file handling out of plugin logic |
| Azure Function | Orchestration and batch processing | Preferred processing engine |

---

## 3. Component Responsibility Map

### 3.1 Model-driven app

#### Responsibility
The model-driven app is the host experience for the user.

#### It should handle

- entry point to the Bulk Processor record
- navigation to PCF or custom page if needed
- display of batch fields
- display of child item grid
- visibility of Submit and Reprocess actions
- role-based access to the feature

#### It should not handle

- bulk creation of Request or Job directly
- file parsing
- heavy validation logic
- orchestration logic

---

### 3.2 PCF control

#### Responsibility
The PCF is responsible for selection UX and lightweight item staging initiation.

#### The PCF should handle

- hereditament / SSU search
- multiple selection
- basic duplicate detection in the current selection
- display selected item count
- display basic feedback from the staging API
- invoke the staging API with selected items
- optional filter and search experience for users

#### The PCF should not handle

- bulk Request creation
- bulk Job creation
- long-running processing
- authoritative business validation
- cross-batch duplicate checks unless explicitly cached and lightweight
- direct Dataverse row-by-row synchronous loops for high volume

#### PCF output

The PCF should send a single request to the backend service or API layer containing:

- Batch Id
- Source Type = PCF
- selected identifiers
- optional context

---

### 3.3 CSV upload mechanism

#### Responsibility
CSV upload is responsible only for:

- file capture
- file storage reference
- handing off to a parser or orchestrator

#### It should handle

- attach or upload file to approved storage
- associate file reference with Bulk Processor
- trigger parsing process

#### It should not handle

- parsing in the browser
- row-by-row business processing in the UI
- large synchronous transformations in client code

#### Recommendation

For MVP:

- upload the file to SharePoint
- store the reference in `voa_filereference`
- let Azure Function parse and stage

---

### 3.4 Dataverse tables

#### Responsibility
Dataverse stores:

- batch state
- line state
- counts
- references to created records
- audit trail at business level

#### Tables in scope

- Bulk Processor
- Bulk Processor Item
- existing Request
- existing Job

#### Dataverse should remain the source of truth for

- status values
- counts
- timestamps
- request and job references
- processing attempts
- duplicate outcome

---

### 3.5 Dataverse plugins

#### Responsibility
I want plugins to enforce transaction-safe rules that must happen inside Dataverse.

#### Plugin responsibilities

- validate required batch fields before submit
- block illegal status transitions
- lock a batch during submission and processing
- enforce item-level defaults on create
- prevent direct edits when records are locked
- enforce server-side consistency for status changes
- update Bulk Processor file metadata after a successful upload response

#### What the plugin should not do

- perform the SharePoint upload itself
- hold long-running file transfer logic
- manage large file payload handling directly

#### Why plugins are important

Some rules need to execute synchronously and close to the data. I do not want those rules pushed into Azure Function or spread across the UI.

---

### 3.6 Custom API layer

#### Responsibility
The Custom API layer should encapsulate business operations that need to be reusable and controlled.

I want three APIs only.

#### API 1 - `voa_UploadBulkProcessorFile`
Used to validate the upload request, call the backend integration service, and update Bulk Processor file metadata from the upload response.

#### API 2 - `voa_CreateBulkProcessorItems`
Used to stage selected or parsed rows into Bulk Processor Item.

#### API 3 - `voa_CreateRequestAndJobFromBulkProcessorItem`
Used to process one staged item into the real business records.

### Why this split is important

This gives me:

- clean separation between upload, staging, and processing
- reusable business logic
- easier future evolution of the orchestration layer
- reduced duplication across entry paths
- smaller and safer transaction boundaries

---

### 3.7 Azure Function orchestration

#### Responsibility
Azure Function handles:

- reading submitted batches
- parsing staged CSV files
- processing only eligible items
- item locking
- calling Dataverse Custom APIs for each item
- chunking and retries
- batch completion updates
- processing summaries
- moving batch to Completed, Partially Failed, or Failed

#### Important rule

Azure Function does not call plugins directly.

It calls Dataverse Custom APIs, and the plugin logic runs inside Dataverse as part of that Custom API execution.

---

### 3.8 Backend Integration Service

#### Responsibility
The backend integration service handles the actual SharePoint file transfer.

#### It should handle

- receiving the file payload or file stream from the upload API flow
- creating the SharePoint folder path for the batch
- uploading the file to SharePoint
- returning file metadata such as file reference and SharePoint file id

#### It should not handle

- business validation beyond basic file transfer constraints
- Dataverse state management
- batch orchestration
- parsing or item staging

---

## 4. Detailed Responsibility Split

### 4.1 What goes into PCF

| Capability | PCF? | Notes |
|---|---|---|
| Search hereditaments / SSU | Yes | Core UX responsibility |
| Multi-select records | Yes | Core UX responsibility |
| Show current selection count | Yes | UI feedback |
| Basic duplicate check in selected list | Yes | Only local or session-level duplicate check |
| Save selected list to batch | Yes, via API call | Should call backend once |
| Show staged result summary | Yes | Example: 100 received, 95 valid, 5 duplicates |
| Create Request / Job directly | No | Must not do this in bulk |
| Process long-running work | No | Not suitable for UI |

---

### 4.2 What goes into Custom API

| Capability | Custom API? | Notes |
|---|---|---|
| Validate upload request and delegate file transfer | Yes | Single upload transaction entry point |
| Create Bulk Processor Item rows | Yes | Reusable staging API |
| Apply staging validation rules | Yes | Prefer server-side consistency |
| Check duplicates within batch | Yes | Reliable source of truth |
| Create Request | Yes | Business operation |
| Create Job | Yes | Business operation |
| Link Request and Job | Yes | Business operation |
| Resolve assignment | Yes | Business logic |
| Update item result | Yes | Handled as part of processing API |
| Batch chunk scheduling | No | Belongs in Azure Function |

---

### 4.3 What goes into plugins

| Capability | Plugin? | Notes |
|---|---|---|
| Validate batch before submit | Yes | Synchronous control point |
| Block invalid status transitions | Yes | Keeps records consistent |
| Lock batch on submit | Yes | Prevents double action |
| Default item fields on create | Yes | Example: source type inheritance |
| Prevent edits to locked records | Yes | Supports process safety |
| Enforce required assignment mode rules | Yes | Batch-level validation |
| Validate upload eligibility | Yes | For `voa_UploadBulkProcessorFile` |

---

### 4.4 What goes into Azure Function

| Capability | Azure Function? | Notes |
|---|---|---|
| Read CSV file | Yes | Orchestration responsibility |
| Parse rows | Yes | Better outside UI |
| Call `voa_CreateBulkProcessorItems` | Yes | Used for CSV path |
| Pick submitted batches | Yes | Scheduler responsibility |
| Get valid items in chunks | Yes | Processing orchestration |
| Call `voa_CreateRequestAndJobFromBulkProcessorItem` per item | Yes | Main processing loop |
| Retry failed items | Yes | Orchestration responsibility |
| Update batch summary counts | Yes | Or via helper logic |
| Recalculate statuses | Yes | Or via helper logic |
| Implement core Request / Job business rules | No | Should stay in server-side business layer |
| Call plugins directly | No | Azure Function calls Dataverse Custom APIs instead |

---

### 4.5 What goes into the backend integration service

| Capability | Backend Integration Service? | Notes |
|---|---|---|
| Receive file payload or stream | Yes | Called from the upload API flow |
| Build SharePoint folder path | Yes | Batch-based storage structure |
| Upload file to SharePoint | Yes | Core responsibility |
| Return file metadata | Yes | File reference, file id, path, name |
| Update Dataverse batch record | No | Plugin handles this after response |
| Parse CSV rows | No | Azure Function handles this later |

---

## 5. API Contract Design

### 5.1 API: `voa_UploadBulkProcessorFile`

#### Purpose
Validate the upload request, delegate the file transfer to the backend integration service, and update the Bulk Processor with the returned file metadata.

#### Suggested input

```json
{
  "bulkProcessorId": "BP-20260415-003",
  "fileName": "london-intake.csv",
  "contentType": "text/csv",
  "fileContentBase64": "..."
}
```

#### Suggested response

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

#### Main rules

- Must validate that the batch exists
- Must validate that the batch is Draft or otherwise upload-eligible
- Must validate that `Source Type = CSV`
- Must validate that the file extension is `.csv`
- Must call the backend integration service to upload the file
- Must update the Bulk Processor file fields from the returned metadata
- Must return one outcome for the whole operation

---

### 5.2 API: `voa_CreateBulkProcessorItems`

#### Purpose
Create Bulk Processor Item records from PCF selection or CSV-derived rows.

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

#### Suggested response

```json
{
  "bulkProcessorId": "BP-20260415-003",
  "totalReceived": 2,
  "validCount": 2,
  "invalidCount": 0,
  "duplicateCount": 0,
  "items": [
    {
      "sourceValue": "SSU-200101",
      "itemId": "BPI-001",
      "validationStatus": "Valid",
      "message": null
    },
    {
      "sourceValue": "SSU-200102",
      "itemId": "BPI-002",
      "validationStatus": "Valid",
      "message": null
    }
  ]
}
```

#### Main rules

- Must not create duplicate child rows for the same batch
- Must return per-row outcome
- Must update batch counts after staging

---

### 5.3 API: `voa_CreateRequestAndJobFromBulkProcessorItem`

#### Purpose
Take one staged item and attempt final business creation.

#### Suggested input

```json
{
  "bulkProcessorItemId": "BPI-B-005",
  "processingRunId": "RUN-20260415-02"
}
```

#### Suggested response

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

#### Success example

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

#### Main rules

- Only process item if current status = Valid
- Must increment processing attempt count
- Must write request and job references on success
- Must write failure reason on failure

---

## 6. SharePoint Storage Design

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

## 7. File Upload Constraint

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

## 8. What the PCF Should Call

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

## 9. End-to-End Flows

### 9.1 PCF selection flow

1. User creates Bulk Processor record
2. User selects:
   - Source Type = PCF
   - Requested Job Type
   - Assignment Mode
3. User opens PCF
4. User searches and selects hereditaments / SSUs
5. PCF sends selected items to `voa_CreateBulkProcessorItems`
6. API creates Bulk Processor Item rows
7. API returns summary to PCF
8. User reviews staged rows
9. User clicks Submit
10. Batch moves to Submitted
11. Azure Function picks batch later
12. Valid items are processed one by one

### 9.2 CSV flow

1. User creates Bulk Processor record
2. User selects:
   - Source Type = CSV
   - Requested Job Type
   - Assignment Mode
3. User uploads file
4. `voa_UploadBulkProcessorFile` stores the file and updates metadata
5. Azure Function reads the SharePoint file
6. Rows are parsed
7. Azure Function calls `voa_CreateBulkProcessorItems`
8. Child rows are created
9. User reviews staged counts and items
10. User clicks Submit
11. Batch moves to Submitted
12. Azure Function picks valid rows for processing

### 9.3 Processing flow

1. Azure Function identifies Submitted batches
2. Batch is locked using plugin or batch lock logic
3. Valid items are fetched in chunks
4. Each item is locked for processing
5. `voa_CreateRequestAndJobFromBulkProcessorItem` is called
6. Item becomes:
   - Processed, or
   - Failed
7. Counts are recalculated
8. Batch becomes:
   - Completed, or
   - Partially Failed, or
   - Failed

---

## 10. Security and Access Considerations

### Access expectations

| Role | Access |
|---|---|
| Business user | Create batch, upload/select items, view results, submit |
| Processing/admin user | Reprocess failed items, review technical errors |
| System/service account | Run orchestration and update statuses |

### Important controls

- users should not directly edit processed child rows
- only authorised users should submit batches
- reprocess should be restricted
- service identity should own automated processing updates

---

## 11. Logging and Support Design

### What I want logged

#### Batch level

- batch created
- file uploaded
- submit clicked
- processing started
- processing completed
- batch final status

#### Item level

- item staged
- item marked duplicate or invalid
- request creation attempted
- request/job creation result
- failure reason
- reprocess attempt

### Why this matters

This will help support teams answer:

- why a batch failed
- which rows failed
- whether the failure happened at staging or processing
- whether retry is possible

---

## 12. Error Handling Expectations

### Staging errors

Should not fail the whole batch unless the file itself is unusable.

#### Example

- bad row -> mark item Invalid
- duplicate row -> mark item Duplicate

### Processing errors

Should fail at line level where possible.

#### Example

- Request creation fails for one item -> mark item Failed
- continue with other items

### Batch-level failures

Should only happen for true orchestration or runtime problems.

#### Example

- file unreadable
- processing job crashed before any line could run
- batch lock issue

---

## 13. My Implementation Recommendation

### Stable core I want to build first

#### Dataverse

- Bulk Processor
- Bulk Processor Item
- choices, lookups, and relationships

#### Server-side business layer

- `voa_UploadBulkProcessorFile`
- `voa_CreateBulkProcessorItems`
- `voa_CreateRequestAndJobFromBulkProcessorItem`

#### Backend integration service

- SharePoint upload
- file transfer response handling

#### UI

- model-driven app form
- item subgrid
- PCF for multi-select path

#### Orchestration

- Azure Function for batch pickup, parsing, retries, and summary updates

---

## 14. My Recommended Delivery Sequence

### Phase 1

- build tables
- configure choices and lookups
- implement batch form
- implement item subgrid
- create submit command or action

### Phase 2

- build backend integration service
- build `voa_UploadBulkProcessorFile`
- wire PCF to upload API
- test upload path end to end

### Phase 3

- finish CSV file storage and parsing flow
- implement CSV parsing in Azure Function
- stage rows using `voa_CreateBulkProcessorItems`

### Phase 4

- implement `voa_CreateRequestAndJobFromBulkProcessorItem`
- wire processing orchestration
- update item outcomes

### Phase 5

- counts recalculation
- final batch status logic
- retry / reprocess support
- support logging

---

## 15. Summary

My intended technical split is:

- PCF for search, selection, and staging initiation
- Dataverse as the source of truth
- Dataverse plugins for synchronous validation and locking
- Custom APIs for reusable business actions
- backend integration service for SharePoint upload
- Azure Function as the preferred orchestration engine
- Request and Job creation always handled server-side, not directly in the UI

This gives me a design that is realistic for MVP, but also capable of being scaled later without rewriting the whole solution.

---

## 16. Next Step

The next logical step is Jira delivery breakdown.

I should now convert this into:

- epics
- feature stories
- technical tasks

grouped by:

- Dataverse
- PCF
- Custom API
- plugins
- CSV handling
- orchestration
- security
- support and monitoring
