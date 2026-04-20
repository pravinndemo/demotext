# Bulk Ingestion: Final Dataverse Table Design and Validation Rules

I am freezing the **core Dataverse design** first, before moving on to UI and orchestration.

This version focuses on:

- table structure
- field types
- required rules
- status values
- status transitions
- validation rules
- where each validation happens

---

## 1. Design Principles

### Core principles

1. One Bulk Ingestion equals one batch.
2. One Bulk Ingestion Item equals one selected or uploaded row.
3. Do not create `voa_requestlineitem` or incident (Job) records directly from the PCF in bulk.
4. Use Bulk Ingestion Item as the staging layer.
5. Keep staging validation separate from processing validation.
6. Track batch-level outcome and line-level outcome separately.
7. Allow partial success.
8. Keep the first release simple, but make it extensible.

---

## 2. Final Tables

### 2.1 Bulk Ingestion

This is the **header / master** table.

#### Purpose
Stores the overall batch, source, ownership, counts, and processing state.

#### Final column definition

| Schema Name | Display Name | Type | Required | Editable | Example Value | Notes |
|---|---|---|---|---|---|---|
| voa_bulkingestionid | Bulk Ingestion | Unique Identifier | Yes | System | BP-20260415-001 | Primary key |
| voa_batchname | Batch Name | Text (200) | Yes | Yes | Batch A - Clean CSV Upload | User-friendly name |
| voa_batchreference | Batch Reference | Text (100) | Yes | System / optional editable | BULK-20260415-001 | Business reference |
| voa_source | Source | Choice | Yes | Yes before submit | CSV | Sync with global choice: `CSV`, `External System`, `System Entered` |
| voa_template | Template | Lookup (Bulk Ingestion Template) | No | System |  | Optional template reference |
| voa_processingjobtype | Processing Job Type | Lookup (voa_codedreason) | Yes | Yes before submit | Data Enhancement | Lookup to voa_codedreason |
| voa_assignmentmode | Assignment Mode | Choice | Yes | Yes before submit | Team | `Team`, `Manager` |
| voa_assignedteam | Assigned Team | Lookup | Conditionally Yes | Yes before submit | Bulk Processing Team | Required when assignment mode = Team |
| voa_assignedmanager | Assigned Manager | Lookup | Conditionally Yes | Yes before submit | Sarah Jones | Required when assignment mode = Manager |
| voa_filereference | File Reference | Text / URL (500) | No | System | sharepoint://bulk/batch-a-clean.csv | Only relevant for CSV |
| voa_fileoriginalname | File Original Name | Text (255) | No | System | batch-a-clean.csv | Helpful for support |
| voa_totalrows | Total Rows | Whole Number | Yes | System | 5 | Total child items |
| voa_validitemcount | Valid Item Count | Whole Number | Yes | System | 5 | Passed staging validation |
| voa_invaliditemcount | Invalid Item Count | Whole Number | Yes | System | 0 | Invalid rows |
| voa_duplicateitemcount | Duplicate Item Count | Whole Number | Yes | System | 0 | Duplicates detected |
| voa_processeditemcount | Processed Item Count | Whole Number | Yes | System | 5 | Successfully processed |
| voa_faileditemcount | Failed Item Count | Whole Number | Yes | System | 0 | Failed during processing |
| voa_submittedon | Submitted On | Date and Time | No | System | 2026-04-15 20:00 | Set on submission |
| voa_processingstartedon | Processing Started On | Date and Time | No | System | 2026-04-15 20:05 | Set when job starts |
| voa_processedon | Processed On | Date and Time | No | System | 2026-04-15 20:20 | Set when job completes |
| voa_processingrunid | Processing Run Id | Text (100) | No | System | RUN-20260415-01 | Useful for tracking re-runs |
| voa_errorsummary | Error Summary | Multiline Text | No | System / admin | 1 invalid row and 1 failed request creation | Batch summary only |
| voa_lastactionby | Last Action By | Lookup (User) | No | System | John Smith | Optional audit helper |
| voa_lastactionon | Last Action On | Date and Time | No | System | 2026-04-15 20:20 | Optional audit helper |
| voa_RetriggerFailed | Re-trigger Failed | Two Options | No | Admin / system | No | Optional future flag |
| voa_DelayProcessingUntil | Delay Processing Until | Date and Time | No | Admin / system |  | Optional scheduling control |
| voa_ActualStartTime | Actual Start Time | Date and Time | No | System | 2026-04-15 20:05 | Runtime audit timestamp |
| voa_ActualCompletionTime | Actual Completion Time | Date and Time | No | System | 2026-04-15 20:20 | Runtime audit timestamp |
| statecode | Status | Choice | Yes | System | Active | OOTB Dataverse state: `Active` (0), `Inactive` (1) |
| statuscode | Status Reason | Choice | Yes | System | Draft | OOTB status reason. Active reasons: Draft, Items Created, Submitted, Processing, Partially Failed. Inactive reasons: Completed, Failed, Cancelled |

---

### 2.2 Bulk Ingestion Item

This is the **child / detail** table.

#### Purpose
Stores one row per selected or uploaded hereditament / SSU, including validation and processing result.

#### Final column definition

| Schema Name | Display Name | Type | Required | Editable | Example Value | Notes |
|---|---|---|---|---|---|---|
| voa_bulkingestionitemid | Bulk Ingestion Item | Unique Identifier | Yes | System | BPI-B-005 | Primary key |
| voa_name | Item Name | Text (200) | Yes | System | BPI-B-005 - SSU-200105 | Friendly identifier |
| voa_parentbulkingestion | Parent Bulk Ingestion | Lookup (voa_bulkingestion) | Yes | System | BP-20260415-002 | Parent batch |
| voa_source | Source | Choice | Yes | System | CSV | Copied from parent for easier filtering |
| voa_ssuid | SSU Id | Text (100) | No / conditional | Yes before submit for manual corrections if allowed | SSU-200105 | Main identifier for MVP if used |
| voa_hereditamentref | Hereditament Reference | Text (100) | No | Yes before submit if allowed | HER-778899 | Optional if available |
| voa_sourcevalue | Source Value | Text (255) | Yes | System | SSU-200105 | Raw input as received |
| voa_sourcerownumber | Source Row Number | Whole Number | No | System | 6 | For CSV rows |
| voa_validationstatus | Validation Status | Choice | Yes | System | Failed | Main line-level status |
| voa_validationmessage | Validation Message | Multiline Text | No | System / admin | voarequestlineitem creation failed due to missing mandatory ownership mapping | Line-level explanation |
| voa_isduplicate | Is Duplicate | Two Options | Yes | System | Yes | Easy filtering |
| voa_duplicatecategory | Duplicate Category | Choice | No | System | Same Batch | `Same Batch`, `Existing Open Batch`, `Existing Active Work`, etc. |
| voa_assignedteam | Assigned Team | Lookup | Conditionally Yes | System / before submit if needed | Council Tax Team A | Item can be assigned to either team or user; required when team is selected |
| voa_assignedmanager | Assigned Manager | Lookup (User) | Conditionally Yes | System / before submit if needed | Sarah Jones | Item can be assigned to either team or user; required when user is selected |
| ownerid | Owner | Lookup (User or Team) | Yes | System | Council Tax Team A / Sarah Jones | OOTB owner set from selected assignee; only one owner at a time |
| voa_requestidtext | Request Line Item Id (Text) | Text (100) | No | System | RLI-61006 | Optional text copy for support/reporting |
| voa_jobidtext | Incident Id (Text) | Text (100) | No | System | INC-71006 | Optional text copy for support/reporting |
| voa_requestlookup | Request Line Item (voa_requestlineitem) | Lookup (voa_requestlineitem) | No | System | RLI-61006 | Preferred real relationship |
| voa_joblookup | Job | Lookup (incident) | No | System | INC-71006 | Preferred real relationship |
| voa_processingattemptcount | Processing Attempt Count | Whole Number | Yes | System | 2 | Increment on each processing attempt |
| voa_processingtimestamp | Processing Timestamp | Date and Time | No | System | 2026-04-15 21:25 | Last attempt timestamp |
| voa_processingrunid | Processing Run Id | Text (100) | No | System | RUN-20260415-02 | Useful for tracing |
| voa_canreprocess | Can Reprocess | Two Options | Yes | System | Yes | Useful for failed rows |
| voa_lockedforprocessing | Locked For Processing | Two Options | Yes | System | No | Helps avoid double processing |
| voa_rawpayload | Raw Payload | Multiline Text | No | System | {"SSU_ID":"SSU-200105"} | Optional future support field |
| voa_processingstage | Processing Stage | Choice | No | System | Validation | Values: `Staging`, `Validation`, `voa_requestlineitem Creation`, `Job Creation`, `Completed` |

---

### 2.3 Bulk Ingestion Template

This is the template / schema helper table for predefined ingestion formats.

#### Final column definition

| Schema Name | Display Name | Type | Required | Editable | Example Value | Notes |
|---|---|---|---|---|---|---|
| voa_bulkingestiontemplateid | Bulk Ingestion Template | Unique Identifier | Yes | System | (GUID) | Primary key |
| voa_name | Template Name | Text (200) | Yes | Yes | CSV Standard Template | Primary name column |
| voa_format | Format | Choice | No | System | CSV | Values: `CSV`, `External System`, `System Entered` (synced with global choice **Bulk ingestion source**) |

---

## 3. Required Field Rules

### 3.1 Bulk Ingestion required rules

| Field | Required Rule |
|---|---|
| Batch Name | Always required |
| Batch Reference | Always required |
| Source | Always required |
| Status | Always required |
| Processing Job Type | Always required |
| Assignment Mode | Always required |
| Assigned Team | Required when Assignment Mode = Team |
| Assigned Manager | Required when Assignment Mode = Manager |
| File Reference | Required when Source = CSV and file is uploaded |
| Count fields | Always system-managed |
| Submitted On / Processing dates | Only required when relevant status is reached |

### 3.2 Bulk Ingestion Item required rules

| Field | Required Rule |
|---|---|
| Bulk Ingestion | Always required |
| Source | Always required |
| Source Value | Always required |
| Validation Status | Always required |
| SSU Id | Required if SSU Id is the chosen primary identifier for the source |
| Source Row Number | Required for CSV items, not required for System Entered items |
| Assigned Team / Assigned Manager | Exactly one required for item assignment (team or user) |
| Owner | Always required; set from selected assignee |
| Duplicate fields | System-managed |
| Processing Attempt Count | Always required, default = 0 |
| Can Reprocess | Always required, default = Yes |
| Locked For Processing | Always required, default = No |

---

## 4. Final Status Model

### 4.1 Bulk Ingestion statuses

| Status | Meaning |
|---|---|
| Status | State | Meaning |
|---|---|---|
| Draft | Active (0) | Batch created but not yet ready for processing |
| Items Created | Active (0) | Child items successfully staged |
| Submitted | Active (0) | Batch locked and waiting for processing |
| Processing | Active (0) | Background process is currently running |
| Partially Failed | Active (0) | Processing finished with mixed outcome |
| Completed | Inactive (1) | Processing finished successfully for all eligible rows |
| Failed | Inactive (1) | Batch-level failure prevented normal processing |
| Cancelled | Inactive (1) | Manual cancellation |

#### Recommended MVP statuses

For MVP, use:

- Draft
- Items Created
- Submitted
- Processing
- Partially Failed
- Completed
- Failed
- Cancelled

> **Note:** Completed, Failed, and Cancelled set the record to `Inactive` state (statecode = 1). All others are `Active` state (statecode = 0). This uses OOTB `statecode`/`statuscode` — there is no separate `voa_status` column.

---

### 4.2 Bulk Ingestion Item statuses

| Validation Status | Meaning |
|---|---|
| Pending | Item created but not fully validated or not yet submitted |
| Valid | Passed staging validation and ready for processing |
| Invalid | Failed staging validation |
| Duplicate | Duplicate item identified |
| Processed | Request and Job created successfully |
| Failed | Processing attempted but failed |

#### Recommended MVP item statuses

For MVP, use:

- Pending
- Valid
- Invalid
- Duplicate
- Processed
- Failed

---

## 5. Status Transition Rules

### 5.1 Bulk Ingestion transitions

| From | To | Rule |
|---|---|---|
| Draft | Items Created | At least one child item created |
| Draft | Failed | Batch-level creation or import failure |
| Items Created | Submitted | User submits and header validations pass |
| Submitted | Processing | Worker or job picks up the batch |
| Processing | Completed | All eligible items processed successfully |
| Processing | Partially Failed | At least one item failed and at least one succeeded |
| Processing | Failed | Batch-level failure stops processing |
| Partially Failed | Processing | Reprocess run starts |
| Failed | Draft or Submitted | Admin / manual recovery only if allowed |

Lifecycle rule:
If a parent `Bulk Ingestion` record is set to `Inactive`, all related `Bulk Ingestion Item` records must also be set to `Inactive`.

### 5.2 Bulk Ingestion Item transitions

| From | To | Rule |
|---|---|---|
| Pending | Valid | Staging validation passed |
| Pending | Invalid | Staging validation failed |
| Pending | Duplicate | Duplicate identified |
| Valid | Processed | Request + Job created successfully |
| Valid | Failed | Processing attempt failed |
| Failed | Processed | Reprocess succeeded |
| Failed | Failed | Reprocess failed again |

---

## 6. Editability Rules

### 6.1 Bulk Ingestion editability

| Status | Editable? | Notes |
|---|---|---|
| Draft | Yes | User can update header fields |
| Items Created | Limited | User can still edit some fields before submit |
| Submitted | No | Lock business fields |
| Processing | No | Read-only |
| Completed | No | Read-only |
| Partially Failed | Limited / admin only | Allow reprocess-related actions only |
| Failed | Limited / admin only | Recovery actions only |

#### Recommended editable fields before submit

Allow edits to:

- Batch Name
- Processing Job Type
- Assignment Mode
- Assigned Team / Assigned Manager

Do not allow editing of:

- counts
- file metadata
- submitted / processed timestamps
- system status (statecode / statuscode)

---

### 6.2 Bulk Ingestion Item editability

| Status | Editable? | Notes |
|---|---|---|
| Pending | Optional limited edit | Only if business wants correction before submit |
| Valid | No | Better to keep system-controlled |
| Invalid | Optional admin correction | Prefer re-upload / reselect over manual fix |
| Duplicate | No | System-controlled |
| Processed | No | Locked |
| Failed | Admin / system only | For reprocess support |

#### Recommended MVP rule

For MVP:

- do not allow business users to manually edit child rows
- if a row is wrong, remove it and re-upload / reselect
- this keeps the process cleaner

---

## 7. Validation Model

Validation must be split into two layers:

1. Staging validation
2. Processing validation

This is critical.

---

## 8. Staging Validation Rules

Staging validation happens when:

- the system-entered selection is submitted for staging
- CSV rows are parsed and staged

### 8.1 Header-level staging validation

| Rule | Applies To | Outcome if Failed |
|---|---|---|
| Batch Name present | All batches | Cannot save or submit |
| Source selected | All batches | Cannot save or submit |
| Processing Job Type selected | All batches | Cannot submit |
| Assignment Mode selected | All batches | Cannot submit |
| Assigned Team present when Assignment Mode = Team | Team batches | Cannot submit |
| Assigned Manager present when Assignment Mode = Manager | Manager batches | Cannot submit |
| File attached / file reference present when Source = CSV | CSV batches | Cannot create items or submit |
| Batch contains at least one child item before submit | All batches | Cannot submit |

### 8.2 Item-level staging validation

| Rule | Applies To | Outcome if Failed |
|---|---|---|
| Source Value present | All items | Mark Invalid |
| Source Row Number present for CSV | CSV items | Mark Invalid if mandatory |
| Exactly one assignee present (team or user) | All items | Mark Invalid |
| SSU Id format valid | When SSU Id used | Mark Invalid |
| Hereditament exists in source system | If existence check is part of staging | Mark Invalid |
| Duplicate within same batch | All items | Mark Duplicate |
| Duplicate within same uploaded file | CSV items | Mark Duplicate |
| Duplicate already staged in same batch | All items | Mark Duplicate |
| Maximum batch size not exceeded | All items | Batch-level error or reject excess rows |

### 8.3 Recommended staging validation scope for MVP

For MVP, keep staging validation to:

- source value present
- CSV header valid
- SSU Id format valid
- duplicate in same batch/file
- optional existence check if lookup is cheap enough

Do not overload staging with heavy business checks unless they are necessary.

---

## 9. Processing Validation Rules

Processing validation happens when the background worker tries to create the real `voa_requestlineitem` and incident (Job) records.

### 9.1 Header-level processing validation

| Rule | Outcome if Failed |
|---|---|
| Batch status must be Submitted | Batch not picked |
| Batch not already locked by another process | Prevent double-run |
| Assignment data still valid | Batch or line may fail |
| Processing Job Type still supported | Batch or line may fail |

### 9.2 Item-level processing validation

| Rule | Outcome if Failed |
|---|---|
| Item status must be Valid | Skip item |
| Item not already locked for processing | Skip item |
| Owner resolvable from selected team/user | Mark Failed |
| voa_requestlineitem does not already exist for this line if uniqueness is required | Mark Failed or Duplicate depending on rule |
| incident (Job) mapping available for Processing Job Type | Mark Failed |
| Assignment target can be resolved | Mark Failed |
| Mandatory fields for voa_requestlineitem can be derived | Mark Failed |
| Mandatory fields for incident (Job) can be derived | Mark Failed |
| voa_requestlineitem creation succeeds | Continue |
| incident (Job) creation succeeds | Mark Processed |
| Linking voa_requestlineitem and incident succeeds | Mark Processed or Failed depending on transaction design |

---

## 10. Duplicate Rules

Duplicates need to be defined clearly because business users usually ask about them early.

### 10.1 Recommended MVP duplicate categories

| Duplicate Category | Meaning |
|---|---|
| Same Batch | Same item repeated in current batch |
| Same File | Same CSV row value repeated in uploaded file |
| Existing Open Batch | Item already staged in another active batch |
| Existing Active Work | Optional future rule where active Request / Job already exists |

### 10.2 MVP recommendation

For MVP, enforce only:

- duplicate in same batch
- duplicate in same uploaded file

Optional later:

- duplicate against other active batches
- duplicate against existing live voarequestlineitem/incident records

---

## 11. Reprocess Rules

### Recommended MVP reprocess behaviour

| Scenario | Rule |
|---|---|
| Invalid item | Do not reprocess automatically |
| Duplicate item | Do not reprocess automatically |
| Failed item | Can be reprocessed |
| Processed item | Never reprocess |
| Completed batch | Reprocess only failed items if business wants it |
| Partially Failed batch | Allow reprocess of failed rows only |

### Recommended fields to support this

- `voa_canreprocess`
- `voa_processingattemptcount`
- `voa_processingrunid`
- `voa_lockedforprocessing`

---

## 12. Count Calculation Rules

Counts on Bulk Ingestion should be system-managed only.

### Count definitions

| Field | Definition |
|---|---|
| Total Rows | Total child rows under the batch |
| Valid Item Count | Child rows currently in `Valid` |
| Invalid Item Count | Child rows currently in `Invalid` |
| Duplicate Item Count | Child rows currently in `Duplicate` |
| Processed Item Count | Child rows currently in `Processed` |
| Failed Item Count | Child rows currently in `Failed` |

### Important note

If an item moves from `Failed` to `Processed` after reprocess, the counts must be recalculated.

---

## 13. Recommended Dataverse Relationship Design

### Relationships

| Parent | Child | Type | Notes |
|---|---|---|---|
| Bulk Ingestion | Bulk Ingestion Item | 1:N | Core relationship. Lookup: `voa_parentbulkingestion`. Cascade deactivate from parent to child |
| Bulk Ingestion Item | voa_requestlineitem | N:1 or optional 1:1-style | Lookup: `voa_requestlookup`. Depends on voa_requestlineitem model |
| Bulk Ingestion Item | incident (Job) | N:1 or optional 1:1-style | Lookup: `voa_joblookup`. Depends on incident model |
| Bulk Ingestion | Bulk Ingestion Template | N:1 | Lookup: `voa_template` on Bulk Ingestion |

### Recommendation

Use both:

- lookup fields to real voarequestlineitem / incident records
- optional text copy of Request Line Item Id / Incident Id for reporting and support

---

## 14. Recommended Business Rules / Plugin Rules

### Dataverse form / business rule level

Use lightweight enforcement for:

- assignment mode + owner requirement
- submit blocked when no child items exist
- submit blocked when required header fields are missing

### Server-side rule level

Use server-side validation for:

- batch locking
- duplicate enforcement
- count updates
- request / job creation checks
- processing state transitions

---

## 15. Final MVP Scope for Table Design

### Bulk Ingestion: fields to definitely include

- Batch Name
- Batch Reference
- Source
- Status
- Processing Job Type
- Assignment Mode
- Assigned Team
- Assigned Manager
- File Reference
- Total / Valid / Invalid / Duplicate / Processed / Failed counts
- Submitted On
- Processing Started On
- Processed On
- Error Summary
- Processing Run Id

### Bulk Ingestion Item: fields to definitely include

- Parent Bulk Ingestion
- Source
- SSU Id
- Hereditament Reference
- Source Value
- Source Row Number
- Validation Status
- Validation Message
- Is Duplicate
- Duplicate Category
- Assigned Team
- Assigned Manager
- Request lookup
- Job lookup
- Request Id text
- Job Id text
- Processing Attempt Count
- Processing Timestamp
- Locked For Processing
- Can Reprocess

---

## 16. Final Recommendation

### Freeze this now

Freeze:

- tables
- fields
- statuses
- required rules
- staging vs processing validation split

### Do not freeze yet

Leave flexible for later:

- exact duplicate policy across other batches
- exact reprocess UX
- whether child rows can be corrected manually
- whether manager assignment is in MVP or phase 2

---

## 17. Suggested Workshop Summary Wording

“We now have a fixed Dataverse design with one batch header table and one child line table. The header tracks the overall batch and counts, while the child table tracks each selected or uploaded item individually. We have also separated validation into staging validation and processing validation, so business users can clearly see whether a row failed before processing or failed later during Request and Job creation.”

---


