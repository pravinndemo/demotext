# Title
Sync design decisions with Mark and Kal for bulk processor entities and processing flow

# Description
We need a design alignment session with Mark and Kal to confirm the bulk processor data model and the processing approach before implementation continues.

This task should cover the April 16 discussion points and finalize the approach for:
- `Bulk Processor`
- `Bulk Processor Item`
- statuses and validation states
- key columns on both entities
- Azure Function as the background processor
- whether the existing Custom API should be enhanced or redesigned if it is not fit for purpose

Focus areas for the sync:
- Agree the final purpose of `Bulk Processor` as the batch header and `Bulk Processor Item` as the staged child rows.
- Agree the columns needed on both entities, including file reference, input mode, assigned team, job context, request/job lookups, processing summary, and delayed processing fields if needed.
- Confirm the MVP flow from PCF selection or CSV upload through staging and background processing.
- Confirm where validation happens and which status values are required for header and item records.
- Confirm whether Azure Function should own orchestration and batch processing.
- Review the existing Custom API design and decide whether it should be enhanced, split, or replaced if it does not fit the final flow.

Discussion points from April 16 to validate:
1. One batch header table and one child item table.
2. One input mode per batch for MVP, not a hybrid.
3. File storage outside Dataverse, with SharePoint or Blob as the preferred options.
4. Background processing to create `Request` and `Job`, assign to `Team`, and update the parent and child records.
5. Delayed processing as a future enhancement to note in the design.
6. Keep the design generic enough to support future bulk scenarios such as other data enhancements.

Expected output from the sync:
- Agreed entity design for `Bulk Processor` and `Bulk Processor Item`.
- Agreed list of columns and statuses.
- Agreed Azure Function role in the architecture.
- Decision on whether the current Custom API is sufficient or needs enhancement.
- Any design changes that should be reflected in the implementation stories.

## Comments To Update

### What we discussed
- The solution needs two main tables: `Bulk Processor` as the parent batch record and `Bulk Processor Item` as the child staging table.
- The user will use either PCF selection or CSV upload for a batch, but not both together in the MVP.
- File storage should stay outside Dataverse, with SharePoint or Blob as the likely options.
- Azure Function should be the preferred background processor for orchestration, batch pickup, retries, and processing updates.
- The background flow should create `Request` and `Job`, assign the work to a `Team`, and update the parent and child records.
- We also discussed the possibility of delayed processing as a future enhancement.
- We need to review whether the current Custom API design is enough or whether it should be enhanced if it does not fit the final flow.

### What we concluded
- `Bulk Processor` will remain the batch header record.
- `Bulk Processor Item` will hold the staged and validated child rows.
- MVP should support only one input mode per batch, not a hybrid of PCF and CSV.
- Dataverse should not be used for file storage unless the file is very small and tightly controlled.
- Azure Function should own the processing orchestration rather than Power Automate for this flow.
- The current Custom API should be reviewed and extended only if needed; if it is not fit for purpose, we should redesign the split around the final flow.
- The design should stay generic enough to support future bulk scenarios beyond the current case.
