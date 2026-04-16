# Title
Define the overall bulk processor flow from PCF or CSV upload to Request and Job creation

# Description
We need one agreed end-to-end flow for the bulk processor feature, based on the April 14 and April 16 discussions.

The flow should cover:
- `Bulk Processor` as the batch header
- `Bulk Processor Item` as the staged child records
- PCF-based selection or CSV upload as the input mode
- file storage and processing triggers
- background creation of `Request` and `Job`
- association to `Team`
- status, counts, validation, and error handling

MVP flow to validate:
1. The user creates a `Bulk Processor` record and selects the job context, assigned team, and input mode.
2. For MVP, use one input mode per batch only.
3. If the user chooses PCF selection, the selected records are staged as `Bulk Processor Item` rows through the agreed custom API path.
4. If the user chooses CSV upload, the file is stored outside Dataverse, then a background process reads it and stages `Bulk Processor Item` rows.
5. Do not mix PCF selection and CSV upload in the same batch for MVP.
6. The background process then picks up validated items, creates the `Request`, creates or links the `Job`, assigns it to the `Team`, and updates the source item.
7. The parent `Bulk Processor` is updated with counts, file reference, processing summary, and final status.

Key design points to explore:
- Whether file storage should be SharePoint or Azure Blob.
- Why Dataverse should not be used for file storage except for very small controlled cases.
- Which component should trigger processing after file upload.
- Where validation should happen: PCF, Custom API, plugin, or Azure Function.
- How many Custom APIs are needed for staging, processing, and status updates.
- What threshold limits should exist for file size, batch size, row count, and chunk size.
- How duplicates, failures, and retries should be handled.
- Whether delayed processing should be kept as a future enhancement note only.

Options to explore:
1. PCF selection path
   - User selects records in the PCF and the system stages them directly.
   - Good for immediate interaction and simpler MVP control.

2. CSV upload path
   - User uploads a template-based file, the file is stored in SharePoint or Blob, and a background process stages the rows.
   - Better for larger batches and async processing.

3. Shared orchestration path
   - One processing design with separate staging and request/job creation steps.
   - Keeps the solution consistent even if the input mode changes later.

Expected output from the team:
- Recommended overall MVP flow.
- Recommended storage option for uploaded files.
- Recommended threshold limits and validation checkpoints.
- Recommended Custom API split and background processing approach.
- Recommended way to create and link `Request`, `Job`, `Team`, `Bulk Processor`, and `Bulk Processor Item`.
- Any future enhancements to note, including delayed processing.
