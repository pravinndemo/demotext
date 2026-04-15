# Bulk Processor - Submit Rules, Validation Matrix, and Status Transitions

This document captures how the bulk processor behaves from a process perspective.
I have defined:

- when a batch can be submitted
- what validations run at each stage
- how statuses move
- what the system does on success and failure

This is intended to guide both technical implementation and business understanding.

---

## 1. Submit Rules (Batch Level)

### When a batch can be submitted

A batch can move to `Submitted` only if all mandatory conditions are met.

#### Preconditions

| Rule | Description | Outcome if not met |
|---|---|---|
| Batch Name present | I have provided a valid name | Block submission |
| Source Type selected | Either PCF or CSV | Block submission |
| Requested Job Type selected | Job type is chosen | Block submission |
| Assignment Mode selected | Team or Manager selected | Block submission |
| Assignment resolved | Team or Manager populated based on mode | Block submission |
| Child items exist | At least one Bulk Processor Item is present | Block submission |
| CSV file present | Only for CSV batches | Block submission |

---

### Submit action behaviour

#### On Submit (user action)

| Step | System Action |
|---|---|
| 1 | Validate header rules |
| 2 | Validate child items exist |
| 3 | Lock batch for editing |
| 4 | Set status = Submitted |
| 5 | Set Submitted On timestamp |
| 6 | Queue batch for processing (via flow/function) |

#### What gets locked after submission

After submission:

- Batch fields become read-only
- Child items are not editable
- Only system/admin can update statuses

---

## 2. Validation Matrix

This defines what happens at each event.

### 2.1 Create Batch

| Event | Create Bulk Processor |
|---|---|
| Trigger | User creates a new batch |
| Validation | None beyond required fields |
| System Action | Create record with Status = Draft |
| Success Outcome | Batch created |
| Failure Outcome | Standard Dataverse validation error |

### 2.2 Add Items (PCF Selection)

| Event | Add items via PCF |
|---|---|
| Trigger | User selects hereditaments |
| Validation | Source value present, duplicate in same selection |
| System Action | Create Bulk Processor Item rows, mark status = Pending / Valid / Duplicate |
| Success Outcome | Items created with validation result |
| Failure Outcome | Invalid rows marked with message |

### 2.3 Upload CSV

| Event | Upload CSV |
|---|---|
| Trigger | User uploads file |
| Validation | File present, file format valid, header correct (`SSU_ID`) |
| System Action | Parse file, create Bulk Processor Item rows, set Source Row Number, apply staging validation |
| Success Outcome | Items staged |
| Failure Outcome | Entire upload fails or rows marked Invalid |

### 2.4 Staging Validation (Item Level)

| Event | Item validation |
|---|---|
| Trigger | During PCF/CSV staging |
| Validation | Source value present, SSU format valid, duplicate in batch/file |
| System Action | Set Validation Status: Valid / Invalid / Duplicate, store validation message |
| Success Outcome | Item ready for processing |
| Failure Outcome | Item marked Invalid or Duplicate |

### 2.5 Submit Batch

| Event | Submit |
|---|---|
| Trigger | User clicks Submit |
| Validation | Header rules satisfied, at least one item exists |
| System Action | Set status = Submitted, set Submitted On |
| Success Outcome | Batch queued for processing |
| Failure Outcome | Submission blocked with error |

### 2.6 Start Processing

| Event | Batch picked for processing |
|---|---|
| Trigger | Scheduled job or function |
| Validation | Status = Submitted, not already locked |
| System Action | Set status = Processing, set Processing Started On |
| Success Outcome | Batch moves to processing |
| Failure Outcome | Batch skipped |

### 2.7 Process Item

| Event | Process one Bulk Processor Item |
|---|---|
| Trigger | Worker picks item |
| Validation | Status = Valid, not locked |
| System Action | 1. Lock item  2. Validate business rules  3. Create Request  4. Create Job  5. Link Request and Job  6. Assign Team/Manager |
| Success Outcome | Status = Processed, store Request Id / Job Id |
| Failure Outcome | Status = Failed, store error message, increment attempt count |

### 2.8 Complete Batch

| Event | Batch completion |
|---|---|
| Trigger | All items processed |
| Validation | None |
| System Action | Calculate counts, update status |
| Success Outcome | Completed (if all processed), Partially Failed (if mix) |
| Failure Outcome | Failed (if batch-level failure) |

### 2.9 Reprocess Failed Items

| Event | Reprocess |
|---|---|
| Trigger | Admin/system action |
| Validation | Item status = Failed, Can Reprocess = Yes |
| System Action | Reset item lock, retry processing |
| Success Outcome | Item moves to Processed |
| Failure Outcome | Remains Failed |

---

## 3. Status Transition Matrix

### 3.1 Bulk Processor

| From | To | Trigger |
|---|---|---|
| Draft | Items Created | Items added |
| Draft | Failed | Creation/import failure |
| Items Created | Submitted | User submits |
| Submitted | Processing | Job starts |
| Processing | Completed | All items processed |
| Processing | Partially Failed | Mixed outcome |
| Processing | Failed | Batch-level error |
| Partially Failed | Processing | Reprocess |
| Failed | Draft / Submitted | Admin recovery |

### 3.2 Bulk Processor Item

| From | To | Trigger |
|---|---|---|
| Pending | Valid | Validation passed |
| Pending | Invalid | Validation failed |
| Pending | Duplicate | Duplicate found |
| Valid | Processed | Success |
| Valid | Failed | Processing error |
| Failed | Processed | Reprocess success |

---

## 4. Duplicate Rules (MVP)

For the first release, I am limiting duplicates to:

| Rule | Behaviour |
|---|---|
| Duplicate in same batch | Mark as Duplicate |
| Duplicate in same file | Mark as Duplicate |

### Not included in MVP

- Duplicate across other batches
- Duplicate against existing Requests/Jobs

These can be added later if required.

---

## 5. Reprocess Rules

| Scenario | Behaviour |
|---|---|
| Invalid item | Not reprocessed |
| Duplicate item | Not reprocessed |
| Failed item | Can be reprocessed |
| Processed item | Never reprocessed |

---

## 6. Locking Behaviour

### Batch level

- Locked after submission
- No edits allowed during processing

### Item level

- Locked when picked for processing
- Prevents duplicate execution

---

## 7. Counts Update Logic

Counts are always system-calculated.

| Count | Based on |
|---|---|
| Total | All items |
| Valid | Items with status = Valid |
| Invalid | Items with status = Invalid |
| Duplicate | Items with status = Duplicate |
| Processed | Items with status = Processed |
| Failed | Items with status = Failed |

---

## 8. Summary

I have separated staging validation and processing validation to avoid confusion.

I am allowing partial success, not blocking the whole batch.

I am keeping duplicate rules simple for MVP.

I am ensuring no bulk synchronous processing from the UI.

I am keeping reprocessing controlled and predictable.

---
