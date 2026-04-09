

---

# 🎯 🧾 MAIN JIRA TASK

## **Title**

Bulk Creation of Data Enhancement Requests and Jobs in VOS (UI-driven)

---

## **Type**

Epic / Feature (depending on your board)

---

## **Description**

As part of Welsh Revaluation data enhancement activities, the system must support **bulk creation of Data Enhancement requests and jobs directly within VOS**, removing the need for manual job creation.

This capability will allow users to:

* search and select multiple hereditaments (e.g. via postcode/address)
* trigger bulk creation of requests and jobs
* assign work to a manager/team
* process jobs via the standard VOS workflow

This replaces reliance on spreadsheet-based approaches and ensures work is managed entirely within VOS.

---

## **Business Context**

Currently:

* Creating a single request/job takes ~7–8 minutes
* Large volumes (1000s of properties) make manual creation inefficient

Target:

* Enable **bulk creation (100–1000 records at once)**
* Maintain:

  * auditability
  * QA process
  * existing job lifecycle

---

## **Scope**

### ✅ In Scope

* UI-based selection of properties (postcode/address search)
* Multi-select of hereditaments (with limit)
* Bulk creation of:

  * Request
  * Data Enhancement Job
* Assignment to:

  * Manager / Team queue
* Asynchronous processing

---

### ❌ Out of Scope

* PAD updates via bulk upload
* Auto-completion of jobs
* Spreadsheet-based processing (future fallback only)
* Changes to existing job lifecycle

---

## **High-Level Flow**

```text
Search properties → Select (max limit) → Provide required fields
→ Submit → Background processing → Requests & Jobs created
→ Assigned → Caseworker picks from queue
```

---

## **Acceptance Criteria**

1. User can search properties using postcode/address
2. User can select multiple hereditaments (with max limit enforced)
3. User can provide required inputs (e.g. assignment)
4. System creates:

   * 1 request per hereditament
   * 1 job per hereditament
5. Jobs are assigned correctly (team/manager)
6. Processing is asynchronous (no UI blocking)
7. System provides success/failure feedback
8. No impact to existing VOS job lifecycle

---

## **Dependencies**

* Confirmation of mandatory fields for request creation
* Data source for property search (PostgreSQL / VOS lookup)
* Assignment routing rules (Michelle team)

---

## **Risks**

* Performance impact for large batches
* Incorrect mandatory field handling
* Duplicate job creation

---

---

# 🔧 🧠 SUBTASK (TECH INVESTIGATION)

## **Title**

Technical Investigation – Bulk Job Creation Capability in VOS

---

## **Type**

Technical Spike / Investigation

---

## **Description**

Investigate and define the technical approach to implement bulk creation of Data Enhancement requests and jobs within VOS.

The solution is expected to support UI-driven selection (PCF/custom page) and asynchronous processing.

---

## **Objectives**

1. Identify how requests and jobs are currently created in VOS
2. Define reusable API/payload for bulk creation
3. Evaluate UI approach (PCF vs Custom Page)
4. Define async processing mechanism
5. Identify mandatory fields and validation rules
6. Assess performance limits and batching strategy

---

## **Key Investigation Areas**

### 🔹 1. Request & Job Creation Flow

* Existing entities:

  * Request
  * Job
* Plugins / Custom APIs involved
* Mandatory fields required

---

### 🔹 2. UI Approach

* PCF control vs Custom Page
* Capability:

  * search
  * multi-select
  * max selection limit

---

### 🔹 3. Data Source for Property Search

* PostgreSQL (PAD store)
* Existing “Find Address” lookup
* DAP2 API possibility

---

### 🔹 4. Bulk Processing Strategy

* Async plugin vs Azure Function
* Queue-based processing
* Batch size handling (e.g. 1000 records)

---

### 🔹 5. API / Payload Design

Define reusable payload:

```json
{
  "hereditamentIds": [],
  "jobType": "DataEnhancement",
  "assignedTo": "manager/team",
  "metadata": {}
}
```

---

### 🔹 6. Validation Rules

* Mandatory fields
* Property existence
* Duplicate detection

---

### 🔹 7. Assignment Logic

* Queue vs Manager
* Alignment with existing routing

---

### 🔹 8. Error Handling

* Partial success
* Failure reporting

---

## **Deliverables**

* Proposed architecture (UI + API + processing)
* Identified mandatory fields
* API contract (input/output)
* Recommended approach (PCF + Async processing)
* Risks and limitations
* Effort estimation (tactical vs strategic)

---

## **Definition of Done**

* Technical approach documented
* Dependencies identified
* Architecture agreed with stakeholders (incl. Mark)
* Ready for detailed design / implementation

---

# 🎯 Final Insight (for your discussion)

👉 Main task = **Business capability (what we are building)**
👉 Subtask = **How we build it (tech spike)**

---



Just tell 👍
