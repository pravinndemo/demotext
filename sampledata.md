# Bulk Processor sample workshop data

Use only **3 example batches** in workshops so the business can clearly understand the different outcomes without too much noise.

---

## Batch A — Clean batch

**Purpose:** show the happy path where everything works.

### Batch summary

| Field | Value |
|---|---|
| Batch Name | Batch A - Clean CSV Upload |
| Source Type | CSV |
| Status | Completed |
| Requested Job Type | Data Enhancement |
| Assignment Mode | Team |
| Team / Manager | Bulk Processing Team |
| Total Item Count | 5 |
| Valid Item Count | 5 |
| Failed Item Count | 0 |

### Example Bulk Processor row

| Bulk Processor Id | Batch Name | Source Type | Status | Requested Job Type | Assignment Mode | Team / Manager | File Reference | Total Item Count | Valid Item Count | Failed Item Count | Submitted On | Processed On |
|---|---|---|---|---|---|---|---|---:|---:|---:|---|---|
| BP-20260415-001 | Batch A - Clean CSV Upload | CSV | Completed | Data Enhancement | Team | Bulk Processing Team | sharepoint://bulk/batch-a-clean.csv | 5 | 5 | 0 | 2026-04-15 20:00 | 2026-04-15 20:20 |

### Example Bulk Processor Item rows

| Bulk Processor Item Id | Bulk Processor Id | SSU Id / Hereditament Ref | Source Row Number | Source Value | Validation Status | Validation Message | Request Id | Job Id | Assigned Team / Manager | Processing Attempt Count | Processing Timestamp |
|---|---|---|---:|---|---|---|---|---|---|---:|---|
| BPI-A-001 | BP-20260415-001 | SSU-100101 | 2 | SSU-100101 | Processed | Request and Job created successfully | REQ-60001 | JOB-70001 | Bulk Processing Team | 1 | 2026-04-15 20:05 |
| BPI-A-002 | BP-20260415-001 | SSU-100102 | 3 | SSU-100102 | Processed | Request and Job created successfully | REQ-60002 | JOB-70002 | Bulk Processing Team | 1 | 2026-04-15 20:07 |
| BPI-A-003 | BP-20260415-001 | SSU-100103 | 4 | SSU-100103 | Processed | Request and Job created successfully | REQ-60003 | JOB-70003 | Bulk Processing Team | 1 | 2026-04-15 20:10 |
| BPI-A-004 | BP-20260415-001 | SSU-100104 | 5 | SSU-100104 | Processed | Request and Job created successfully | REQ-60004 | JOB-70004 | Bulk Processing Team | 1 | 2026-04-15 20:13 |
| BPI-A-005 | BP-20260415-001 | SSU-100105 | 6 | SSU-100105 | Processed | Request and Job created successfully | REQ-60005 | JOB-70005 | Bulk Processing Team | 1 | 2026-04-15 20:18 |

---

## Batch B — Mixed batch

**Purpose:** show realistic processing where not all rows succeed.

### Batch summary

| Field | Value |
|---|---|
| Batch Name | Batch B - Mixed Outcomes |
| Source Type | CSV |
| Status | Partially Failed |
| Requested Job Type | Data Enhancement |
| Assignment Mode | Team |
| Team / Manager | Council Tax Team A |
| Total Item Count | 6 |
| Valid Item Count | 4 |
| Failed Item Count | 2 |

### Example Bulk Processor row

| Bulk Processor Id | Batch Name | Source Type | Status | Requested Job Type | Assignment Mode | Team / Manager | File Reference | Total Item Count | Valid Item Count | Failed Item Count | Submitted On | Processed On |
|---|---|---|---|---|---|---|---|---:|---:|---:|---|---|
| BP-20260415-002 | Batch B - Mixed Outcomes | CSV | Partially Failed | Data Enhancement | Team | Council Tax Team A | sharepoint://bulk/batch-b-mixed.csv | 6 | 4 | 2 | 2026-04-15 21:00 | 2026-04-15 21:40 |

### Example Bulk Processor Item rows

| Bulk Processor Item Id | Bulk Processor Id | SSU Id / Hereditament Ref | Source Row Number | Source Value | Validation Status | Validation Message | Request Id | Job Id | Assigned Team / Manager | Processing Attempt Count | Processing Timestamp |
|---|---|---|---:|---|---|---|---|---|---|---:|---|
| BPI-B-001 | BP-20260415-002 | SSU-200101 | 2 | SSU-200101 | Processed | Request and Job created successfully | REQ-61001 | JOB-71001 | Council Tax Team A | 1 | 2026-04-15 21:05 |
| BPI-B-002 | BP-20260415-002 | SSU-999999 | 3 | SSU-999999 | Invalid | SSU Id not found in source system |  |  | Council Tax Team A | 0 |  |
| BPI-B-003 | BP-20260415-002 | SSU-200103 | 4 | SSU-200103 | Duplicate | Duplicate within same batch |  |  | Council Tax Team A | 0 |  |
| BPI-B-004 | BP-20260415-002 | SSU-200104 | 5 | SSU-200104 | Processed | Request and Job created successfully | REQ-61004 | JOB-71004 | Council Tax Team A | 1 | 2026-04-15 21:15 |
| BPI-B-005 | BP-20260415-002 | SSU-200105 | 6 | SSU-200105 | Failed | Request creation failed due to missing mandatory ownership mapping |  |  | Council Tax Team A | 2 | 2026-04-15 21:25 |
| BPI-B-006 | BP-20260415-002 | SSU-200106 | 7 | SSU-200106 | Processed | Request and Job created successfully | REQ-61006 | JOB-71006 | Council Tax Team A | 1 | 2026-04-15 21:32 |

### What this batch demonstrates

- one item is **invalid** before processing
- one item is a **duplicate**
- some items are **processed successfully**
- one item **fails during request creation**
- header status becomes **Partially Failed**

---

## Batch C — Draft batch

**Purpose:** show a batch that has been created but not yet submitted.

### Batch summary

| Field | Value |
|---|---|
| Batch Name | Batch C - Draft PCF Selection |
| Source Type | PCF |
| Status | Draft |
| Requested Job Type | Data Enhancement |
| Assignment Mode | Manager |
| Team / Manager | Sarah Jones |
| Total Item Count | 3 |
| Valid Item Count | 0 |
| Failed Item Count | 0 |

### Example Bulk Processor row

| Bulk Processor Id | Batch Name | Source Type | Status | Requested Job Type | Assignment Mode | Team / Manager | File Reference | Total Item Count | Valid Item Count | Failed Item Count | Submitted On | Processed On |
|---|---|---|---|---|---|---|---|---:|---:|---:|---|---|
| BP-20260415-003 | Batch C - Draft PCF Selection | PCF | Draft | Data Enhancement | Manager | Sarah Jones |  | 3 | 0 | 0 |  |  |

### Example Bulk Processor Item rows

| Bulk Processor Item Id | Bulk Processor Id | SSU Id / Hereditament Ref | Source Row Number | Source Value | Validation Status | Validation Message | Request Id | Job Id | Assigned Team / Manager | Processing Attempt Count | Processing Timestamp |
|---|---|---|---:|---|---|---|---|---|---|---:|---|
| BPI-C-001 | BP-20260415-003 | SSU-300101 |  | SSU-300101 | Pending | Awaiting submission |  |  | Sarah Jones | 0 |  |
| BPI-C-002 | BP-20260415-003 | SSU-300102 |  | SSU-300102 | Pending | Awaiting submission |  |  | Sarah Jones | 0 |  |
| BPI-C-003 | BP-20260415-003 | SSU-300103 |  | SSU-300103 | Pending | Awaiting submission |  |  | Sarah Jones | 0 |  |

### What this batch demonstrates

- user has selected items
- batch exists
- nothing has been submitted yet
- no request or job has been created
- items are still in **Pending** state

---

# Dataverse-style column definition table

## Bulk Processor

| Schema Name | Display Name | Type | Required | Example Value |
|---|---|---|---|---|
| voa_bulkprocessorid | Bulk Processor | Unique Identifier | Yes | BP-20260415-001 |
| voa_name | Batch Name | Text | Yes | Batch A - Clean CSV Upload |
| voa_sourcetype | Source Type | Choice | Yes | CSV |
| voa_status | Status | Choice | Yes | Completed |
| voa_requestedjobtype | Requested Job Type | Choice | Yes | Data Enhancement |
| voa_assignmentmode | Assignment Mode | Choice | Yes | Team |
| voa_assignedteam | Assigned Team | Lookup | No | Bulk Processing Team |
| voa_assignedmanager | Assigned Manager | Lookup | No | Sarah Jones |
| voa_filereference | File Reference | Text / URL | No | sharepoint://bulk/batch-a-clean.csv |
| voa_totalitemcount | Total Item Count | Whole Number | Yes | 5 |
| voa_validitemcount | Valid Item Count | Whole Number | Yes | 5 |
| voa_faileditemcount | Failed Item Count | Yes | Whole Number | 0 |
| voa_submittedon | Submitted On | Date and Time | No | 2026-04-15 20:00 |
| voa_processedon | Processed On | Date and Time | No | 2026-04-15 20:20 |
| voa_processingrunid | Processing Run Id | Text | No | RUN-20260415-01 |
| voa_errorsummary | Error Summary | Multiline Text | No | 1 invalid row and 1 failed request creation |

---

## Bulk Processor Item

| Schema Name | Display Name | Type | Required | Example Value |
|---|---|---|---|---|
| voa_bulkprocessoritemid | Bulk Processor Item | Unique Identifier | Yes | BPI-B-005 |
| voa_bulkprocessor | Bulk Processor | Lookup | Yes | BP-20260415-002 |
| voa_ssuid | SSU Id | Text | Yes | SSU-200105 |
| voa_hereditamentref | Hereditament Reference | Text | No | HER-778899 |
| voa_sourcerownumber | Source Row Number | Whole Number | No | 6 |
| voa_sourcevalue | Source Value | Text | Yes | SSU-200105 |
| voa_validationstatus | Validation Status | Choice | Yes | Failed |
| voa_validationmessage | Validation Message | Multiline Text | No | Request creation failed due to missing mandatory ownership mapping |
| voa_requestidtext | Request Id | Text | No | REQ-61006 |
| voa_jobidtext | Job Id | Text | No | JOB-71006 |
| voa_assignedteam | Assigned Team | Lookup | No | Council Tax Team A |
| voa_assignedmanager | Assigned Manager | Lookup | No | Sarah Jones |
| voa_processingattemptcount | Processing Attempt Count | Whole Number | Yes | 2 |
| voa_processingtimestamp | Processing Timestamp | Date and Time | No | 2026-04-15 21:25 |
| voa_isduplicate | Is Duplicate | Two Options | Yes | Yes |
| voa_duplicatecategory | Duplicate Category | Choice | No | Same Batch |
| voa_requestlookup | Request | Lookup | No | REQ-61006 |
| voa_joblookup | Job | Lookup | No | JOB-71006 |

---

# Suggested workshop wording

## Batch A
"Batch A is our clean example. Everything is valid, everything is submitted, and every item successfully creates both a Request and a Job."

## Batch B
"Batch B shows the realistic scenario. Some rows are invalid, some are duplicates, some are processed successfully, and one fails later during request creation. This helps explain why we need both line-level tracking and batch-level summary counts."

## Batch C
"Batch C shows a batch that has been created from PCF selection but has not yet been submitted. It helps explain the difference between staging work and actually processing it."

---

# Suggested status values

## Bulk Processor statuses
- Draft
- Items Created
- Submitted
- Processing
- Partially Failed
- Completed
- Failed

## Bulk Processor Item statuses
- Pending
- Valid
- Invalid
- Duplicate
- Processed
- Failed
