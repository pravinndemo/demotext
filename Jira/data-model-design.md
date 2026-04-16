# Title
Design the Dataverse data model for Bulk Processor and Bulk Processor Item

# Description
Create and confirm the final Dataverse data model for the bulk processor solution using the approved table details from the final Dataverse design.

This task should focus on the two core tables:
- `Bulk Processor` as the batch header / master table
- `Bulk Processor Item` as the child / detail table

The design should align with the final table definition, including:
- field names and display names
- data types
- required vs optional fields
- editable rules
- status and validation fields
- lookup relationships
- system-managed counts and timestamps

Use the final Dataverse table details as the source of truth for the following areas:
- `Bulk Processor` columns such as batch name, batch reference, source type, status, requested job type, assignment mode, assigned team, assigned manager, file reference, counts, submission dates, processing dates, error summary, and processing run id
- `Bulk Processor Item` columns such as parent batch, source type, SSU Id, hereditament reference, source value, source row number, validation status, validation message, duplicate flags, assigned team, assigned manager, request lookup, job lookup, processing attempt count, processing timestamp, locked for processing, and processing stage
- relationships between `Bulk Processor`, `Bulk Processor Item`, `Request`, and `Job`

Focus areas to confirm:
- Final entity structure and ownership of each field.
- Required and conditional fields for each table.
- Which fields are system-managed and which can be edited before submit.
- How duplicate detection and validation status should be stored.
- How request and job references should be stored, both as lookups and optional text copies.
- Which counts and timestamps must be maintained on the parent record.
- Whether any future enhancement fields should remain in the model now or be deferred.

Expected output from the team:
- Confirmed `Bulk Processor` and `Bulk Processor Item` schema.
- Confirmed list of columns for both entities.
- Confirmed lookup relationships and ownership rules.
- Confirmed required and editable field rules.
- Any changes needed before implementation begins.
