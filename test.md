**Tech Team Questions**
- What is the final server-side pattern: `Custom API + plugin`, `async plugin`, or `plugin + queue`?
- What is the expected max batch size in one submit action: 100, 1,000, 5,000, or more?
- Do we create one `Bulk Processor` parent record and child `Batch Item` records for every row?
- Which tables are needed in Dataverse: `Bulk Processor`, `Batch Item`, `Request`, `Job`?
- What are the exact logical names and lookup relationships between those tables?
- Should Request and Job be created in the same transaction or split into separate steps?
- How do we handle partial success, retry, and failed items?
- Do we need async processing for all batches or only above a threshold?
- What is the status model for a batch: Draft, Queued, Processing, Partial Success, Completed, Failed?
- What audit fields and progress counts need to be stored on the batch?
- How should the plugin receive input from PCF and CSV so both use the same pipeline?
- Is there any need for batch locking to prevent duplicate submissions?
- How should assignment work: queue, team, manager, or configurable routing?
- What are the performance constraints or expected response time for the UI?
- Do we need a progress endpoint so the UI can refresh batch status after submission?

**Client Questions**
- Do you want the user to start with **PCF selection**, **CSV upload**, or both?
- Which is the preferred way to find hereditaments: address, postcode, billing authority, UPRN, or all of them?
- What is the preferred assignment target: team queue, manager, or configurable routing?
- What is the maximum batch size the business wants to work with?
- Do you want the batch to run immediately or in the background?
- Do you want the user to see the created requests and jobs straight away, or only after processing completes?
- What should happen when some rows fail validation but others are valid?
- Do you want failed rows to block the whole batch, or allow partial success?
- Should CSV and PCF produce the same final result and same downstream process?
- Do you need the ability to rerun or amend a failed batch?
- Do you want batch totals visible on the form, for example selected, valid, invalid, requests created, jobs created?
- Should the batch include a record of who submitted it and when?

**BA Questions**
- What is the exact business outcome for Bulk Processor?
- Is the goal to create requests only, jobs only, or both together?
- What is the difference between a batch, a request, and a job in business terms?
- What does “successful” bulk creation mean to the business?
- What is the minimum data required for a row to be accepted?
- What business rules define a valid hereditament selection?
- What should happen to duplicates, missing values, and conflicting rows?
- Should the batch be able to mix multiple search methods or only one per batch?
- What is the expected user journey from finding hereditaments to seeing the resulting work?
- What should the user do when a row is marked for review?
- How should the team pick up the created jobs after bulk creation?
- What reporting or audit trail does the business need after submission?
- Is the requirement only for data enhancement, or should the design support other job types later?
- What is in scope for phase 1 versus phase 2?
- Is this a tactical solution first, with a strategic UI later?

**Things to Consider**
- Keep one backend pipeline for both PCF and CSV.
- Avoid direct browser-side creation of Request and Job records.
- Use asynchronous processing for large batches.
- Design for partial success instead of all-or-nothing failure.
- Store row-level status so failed items can be reviewed and retried.
- Make the batch parent record the audit and progress container.
- Keep the UI simple: search, select, submit, then monitor.
- Decide early whether the batch limit is a hard cap or a soft warning.
- Make the status model visible to the user.
- Ensure the data model can support future bulk types, not only data enhancement.
- Confirm how assignment works before implementing the workflow.
- Define how the UI shows “created”, “queued”, “review”, and “failed”.
- Validate CSV structure before the batch is submitted.
- Make the table columns consistent across PCF and CSV paths.
- Plan for performance when the volume grows from 1,000 to many thousands.

If you want, I can turn this into:
1. a client workshop agenda,
2. a tech discovery checklist,
3. or a BA-ready question sheet in a cleaner format.
