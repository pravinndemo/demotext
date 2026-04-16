# Title
Explore threshold rules and Custom API split for bulk processor orchestration

# Description
We need a clear design for how thresholds are enforced and how the processing flow is broken into Dataverse Custom APIs.

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

Focus areas to explore:
- What threshold limits should exist for file size, row count, batch size, and chunk size.
- What threshold limits should exist for valid rows, duplicate rows, failed rows, and retry handling.
- Where each threshold should be enforced.
- How many Custom APIs are needed to keep the design clean without over-fragmenting the solution.
- How Requests and Jobs should be created and linked to Teams and to the source `Bulk Processor Item`.
- How the `Bulk Processor` and `Bulk Processor Item` records should be updated during each stage.

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
- Any risks or tradeoffs if the APIs are split too finely or kept too broad.
