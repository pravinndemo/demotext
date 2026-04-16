# Title
Explore threshold rules, processing flow, and Custom API split for bulk processor

# Description
We need one agreed flow for bulk processing and a clear design for how thresholds are enforced and how the processing logic is split into Dataverse Custom APIs.

This task should cover the end-to-end orchestration from PCF upload or Azure Function processing through to creating and updating:
- `Bulk Processor`
- `Bulk Processor Item`
- `Request`
- `Job`
- team associations
- request associations

The team should explore where each rule belongs:
- PCF for immediate user feedback
- Azure Function for orchestration, batching, and processing
- Custom API for reusable Dataverse business operations
- plugin logic where synchronous Dataverse enforcement is required

Updated flow to validate against the April 14 and April 16 discussions:
1. The user creates a `Bulk Processor` record and selects the job context, assigned team, and input mode.
2. For MVP, use one input mode per batch only: either PCF selection or CSV upload, not a hybrid of both.
3. If the user selects items in the PCF, create `Bulk Processor Item` rows through the agreed custom API path.
4. If the user uploads a CSV, store the file in the agreed storage location, then let Azure Function or a background processor read it and stage `Bulk Processor Item` rows.
5. The background process should pick up validated `Bulk Processor Item` rows, create the `Request`, create or link the `Job`, assign it to the `Team`, and update the source item.
6. Update the parent `Bulk Processor` with counts, status, file reference, and processing summary as each stage completes.
7. Keep delayed processing as a future enhancement to note in the design, but do not let it block the MVP decision.

Focus areas to explore:
- What threshold limits should exist for file size, row count, batch size, and chunk size.
- What threshold limits should exist for valid rows, duplicate rows, failed rows, and retry handling.
- Where each threshold should be enforced.
- How many Custom APIs are needed to keep the design clean without over-fragmenting the solution.
- How Requests and Jobs should be created and linked to Teams and to the source `Bulk Processor Item`.
- How the `Bulk Processor` and `Bulk Processor Item` records should be updated during each stage.
- Whether the batch size should start with a conservative MVP limit and how that differs from the processing chunk size.
- Whether thresholds should be enforced earlier in PCF or later in Azure Function / Custom API for consistency.

Options to explore:
1. Minimal API split
   - Fewer Custom APIs with broader responsibility.
   - Simpler to call from Azure Function.
   - Needs careful boundary design to avoid a monolithic API.

2. Staged API split
   - Separate APIs for upload/update, staging items, request/job creation, and result updates.
   - Cleaner business boundaries.
   - More maintainable if the processing rules keep growing.

3. Threshold-first design
   - Define limits first, then decide API boundaries around those rules.
   - Helps avoid overbuilding.
   - Useful if the main risk is complexity and not raw functionality.

Expected output from the team:
- Recommended threshold limits.
- Recommended place for each threshold check.
- Recommended number of Custom APIs and their responsibilities.
- Proposed flow for creating Requests and Jobs and associating them to Teams.
- Proposed update flow for `Bulk Processor` and `Bulk Processor Item`.
- Recommended MVP stance on PCF selection versus CSV upload.
- Any future enhancement notes, especially delayed processing.
- Any risks or tradeoffs if the APIs are split too finely or kept too broad.
