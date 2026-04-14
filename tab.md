Here’s the table model I’d use for the Bulk Processor design.

**Relationships**
- `Bulk Processor` 1 to many `Bulk Processor Item`
- Each `Bulk Processor Item` can create 1 `Request`
- Each `Request` can create 1 `Job`

## 1) Bulk Processor
Parent record for one bulk run.

| Column | Purpose | Example |
|---|---|---|
| `voa_bulkprocessorid` | Primary key / batch id | `BP-2026-001` |
| `voa_name` | Batch name shown to users | `Harrow postcode run` |
| `voa_source` | How the batch was created | `CSV` / `PCF` |
| `voa_searchby` | Search basis used to find rows | `Postcode` |
| `voa_ownerid` | Owner / queue / manager | `Bulk Service Queue` |
| `voa_statusreason` | Batch state | `Draft`, `Queued`, `Processing`, `Partial Success` |
| `voa_totalcount` | Total rows in the batch | `24` |
| `voa_validcount` | Valid rows | `23` |
| `voa_invalidcount` | Invalid rows | `1` |
| `voa_requestcount` | Requests created | `23` |
| `voa_jobcount` | Jobs created | `23` |
| `createdon` | When the batch was created | `13/04/2026` |
| `modifiedon` | Last update time | `13/04/2026` |

**Example row**
- `BP-2026-001`
- `Harrow postcode run`
- `CSV`
- `Postcode`
- `Bulk Service Queue`
- `Partial Success`
- `24 total, 23 valid, 1 invalid, 23 requests, 23 jobs`

## 2) Bulk Processor Item
One row per hereditament/property in the batch.

| Column | Purpose | Example |
|---|---|---|
| `voa_bulkprocessoritemid` | Primary key for the batch item | `ITEM-0001` |
| `voa_bulkprocessorid` | Parent batch lookup | `BP-2026-001` |
| `voa_itemno` | Row number in the batch | `1` |
| `voa_uprn` | Property identifier | `202103120` |
| `voa_address` | Property address | `54 Drayton Way, Harrow, HA3 0BT` |
| `voa_searchby` | How this row was found | `Postcode` |
| `voa_validationstatus` | Row validation result | `Valid`, `Review`, `Failed` |
| `voa_errorcode` | Machine-readable error code | `MISSING_UPRN` |
| `voa_errormessage` | Human-readable error | `UPRN is required before create` |
| `voa_isincluded` | Whether the row is included in processing | `Yes` / `No` |
| `voa_requestid` | Linked request record | `REQ-10021` |
| `voa_jobid` | Linked job record | `JOB-80115` |
| `createdon` | When the item was added | `13/04/2026` |

**Example rows**
- `ITEM-0001` -> valid -> `202103120` -> `54 Drayton Way`
- `ITEM-0002` -> valid -> `202103121` -> `53 Drayton Way`
- `ITEM-0003` -> review -> missing UPRN -> not included

## 3) Request
Created from a valid batch item.

| Column | Purpose | Example |
|---|---|---|
| `voa_requestid` | Primary key | `REQ-10021` |
| `voa_name` | Request label | `Bulk request 001` |
| `voa_bulkprocessorid` | Parent batch lookup | `BP-2026-001` |
| `voa_bulkprocessoritemid` | Source item lookup | `ITEM-0001` |
| `voa_requesttype` | Request category | `Data Enhancement` |
| `voa_jobtype` | Job category | `Job` |
| `voa_statusreason` | Request lifecycle status | `Draft`, `Queued`, `Review` |
| `voa_submission` | Source / submission reference | `Bulk Processor` |
| `voa_assignedto` | Queue / team / manager | `Bulk Service Queue` |
| `createdon` | When request was created | `13/04/2026` |

**Example row**
- `REQ-10021`
- batch `BP-2026-001`
- item `ITEM-0001`
- `Data Enhancement`
- `Draft`
- assigned to `Bulk Service Queue`

## 4) Job
Created from the request.

| Column | Purpose | Example |
|---|---|---|
| `voa_jobid` | Primary key | `JOB-80115` |
| `voa_name` | Job label | `Data Enhancement - 54 Drayton Way` |
| `voa_requestid` | Request lookup | `REQ-10021` |
| `voa_bulkprocessorid` | Batch lookup | `BP-2026-001` |
| `voa_bulkprocessoritemid` | Item lookup | `ITEM-0001` |
| `voa_assignedto` | Queue / manager / caseworker | `Team Queue` |
| `voa_statusreason` | Job status | `Queued`, `In Progress`, `Completed`, `Failed` |
| `voa_startedon` | When work started | `13/04/2026` |
| `voa_completedon` | When work completed | blank / `14/04/2026` |
| `createdon` | When job was created | `13/04/2026` |

**Example row**
- `JOB-80115`
- request `REQ-10021`
- batch `BP-2026-001`
- item `ITEM-0001`
- assigned to `Team Queue`
- status `Queued`

## Error handling on the item table
This is the important part for bulk.

| Error Code | Meaning | What happens |
|---|---|---|
| `MISSING_UPRN` | UPRN is blank or invalid | Row stays in `Review` |
| `NO_MATCH_FOUND` | No hereditament found | Row is not created as a request |
| `DUPLICATE_ROW` | Same property appears twice | Keep one, mark duplicate |
| `REQUEST_CREATE_FAILED` | Request insert failed | Mark item failed and log error |
| `JOB_CREATE_FAILED` | Job insert failed | Request may exist, job retried or flagged |

## How the bulk items are handled
- PCF selection rows and CSV rows both become `Bulk Processor Item` records
- Each item gets its own validation status
- Valid rows are passed to the server-side process
- Invalid rows stay visible for review
- The parent batch stores totals and progress
- Requests and jobs are linked back to the batch and the item

If you want, I can next turn this into:
1. a clean client-facing matrix,
2. a Dataverse schema diagram,
3. or a version with only the exact columns we should show in the UI.
