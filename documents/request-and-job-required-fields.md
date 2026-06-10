# Request Creation and Job Creation - Required Fields

## Scope
This document lists what is required to:
1. submit payloads to the bulk processor endpoints, and
2. successfully create Request and Job records in Dataverse.

Sources used:
- OpenAPI contract: OpenAPI/apim-openapi.json
- Runtime validation and processing:
  - Processing/BulkDataProcessor/Activities/BulkDataRequestProcessor.cs
  - Processing/BulkDataProcessor/Routing/BulkDataRouteDecisionBuilder.cs
  - Processing/BulkDataProcessor/Services/RequestJobCreationService.cs
  - Processing/BulkDataProcessor/Services/DirectJobCreationService.cs

## 1) Required fields per endpoint

### A. POST /bulk-data/save-items
Two valid payload patterns are accepted.

Pattern 1: Bulk Selection mode
- bulkProcessorId: required (GUID, non-empty)
- ssuIds: required for this mode, at least 1 item
- Each ssuIds item should contain statutorySpatialUnitId (GUID)

Pattern 2: Bulk File mode
- bulkProcessorId: required (GUID, non-empty)
- ssuIds: omitted

Optional fields for both patterns:
- sourceType
- fileColumnName (defaults to sourcefile/voa_sourcefile behavior in current docs/code)
- requestedBy
- correlationId

Important validation rules:
- Do not mix bulk fields (bulkProcessorId or ssuIds) with SVT fields (ssuId, userId, componentName).
- Batch must be in Draft status.

### B. POST /bulk-data/submit-batch
Payload field requirement:
- bulkProcessorId: required (GUID, non-empty)

Optional payload fields:
- userId
- componentName
- requestedBy
- correlationId
- sourceType

Additional required business preconditions (checked by code):
- Batch status must be Draft.
- Selected template must include Format (voa_format).
- Batch must have items (totalRows > 0).
- Batch must have valid items (validItemCount > 0).
- Job Type must be available (template job type or header processing job type).

### C. POST /bulk-data/svt-single
Required payload fields:
- ssuId: required
- userId: required
- componentName: required

Optional payload fields:
- correlationId
- sourceType
- requestedBy

Important validation rules:
- SVT payload must not include bulkProcessorId.
- If any SVT fields are present, all three are required: ssuId + userId + componentName.

## 2) What is required for Request creation

For successful Request creation in RequestJobCreationService:
- userId must parse as a valid GUID.
- ssuId for each item must parse as a valid GUID.
- Job Type must resolve to a valid coded reason record (or creation fails).
- Duplicate guard: active request with same SSU and Job Type must not already exist.

Request record fields populated by service (from defaults/config/environment):
- SSU lookup (voa_statutoryspatialunitid)
- Coded reason lookup (job type)
- Submitting internal user
- Submitted by account
- Relationship role
- Owner
- Request type
- Date received
- Target date
- Status
- Data source role
- Channel
- Name
- Related billing authority details (when available)

Example (Request creation):

Input used by RequestJobCreationService:

```json
{
  "item": {
    "ssuId": "11111111-1111-1111-1111-111111111111",
    "sourceType": "SVT",
    "sourceValue": "11111111-1111-1111-1111-111111111111"
  },
  "userId": "33333333-3333-3333-3333-333333333333",
  "componentName": "SVT-UI",
  "jobTypeId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
}
```

Resulting request record (representative field map):

```json
{
  "entity": "voa_requestlineitem",
  "voa_statutoryspatialunitid": {
    "entity": "voa_ssu",
    "id": "11111111-1111-1111-1111-111111111111"
  },
  "voa_codedreasonid": {
    "entity": "voa_codedreason",
    "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
  },
  "voa_submittinginternaluserid": {
    "entity": "systemuser",
    "id": "33333333-3333-3333-3333-333333333333"
  },
  "ownerid": {
    "entity": "systemuser",
    "id": "33333333-3333-3333-3333-333333333333"
  },
  "voa_requesttypeid": {
    "entity": "voa_requesttype",
    "id": "<RequestTypeCouncilTax-guid>"
  },
  "statuscode": 589160001,
  "voa_datereceived": "2026-06-10T10:00:00Z",
  "voa_targetdate": "2026-06-11T10:00:00Z",
  "voa_remarks": "SVT-UI",
  "voa_name": "CT: Request, Data Enhancement, <Hereditament>, <BillingAuthority>"
}
```

## 3) What is required for Job creation

Direct Job creation runs after Request creation when createJob is enabled.

Required for successful Job creation:
- A valid Request must already exist.
- Request must contain coded reason lookup.
- Request must contain customer details via either:
  - request ratepayer lookup, or
  - request submitted-by lookup.
- Request should include SSU lookup (used for hereditament link and job SSU reference).

Job record fields populated by service:
- Title and description
- Owner
- Parent request lookup
- Job type lookup
- Request type
- Customer
- Ready for quality checks flag
- SSU lookup
- Data source role
- Channel
- Relationship role
- Submitting internal user
- Optional: remarks, target date, proposed billing authority

Example (Job creation):

Precondition request (minimum needed for job create path):

```json
{
  "requestId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "request": {
    "voa_codedreasonid": {
      "entity": "voa_codedreason",
      "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    },
    "voa_requesttypeid": {
      "entity": "voa_requesttype",
      "id": "<RequestTypeCouncilTax-guid>"
    },
    "voa_statutoryspatialunitid": {
      "entity": "voa_ssu",
      "id": "11111111-1111-1111-1111-111111111111"
    },
    "voa_customeraccountid": {
      "entity": "account",
      "id": "cccccccc-cccc-cccc-cccc-cccccccccccc"
    },
    "ownerid": {
      "entity": "systemuser",
      "id": "33333333-3333-3333-3333-333333333333"
    }
  }
}
```

Resulting job record (representative field map):

```json
{
  "entity": "incident",
  "title": "Data Enhancement - 11111111-1111-1111-1111-111111111111",
  "description": "SVT-initiated job for 11111111-1111-1111-1111-111111111111 from SVT-UI",
  "ownerid": {
    "entity": "systemuser",
    "id": "33333333-3333-3333-3333-333333333333"
  },
  "voa_parentrequestid": {
    "entity": "voa_requestlineitem",
    "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
  },
  "voa_jobtype": {
    "entity": "voa_codedreason",
    "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
  },
  "voa_requesttype": {
    "entity": "voa_requesttype",
    "id": "<RequestTypeCouncilTax-guid>"
  },
  "customerid": {
    "entity": "account",
    "id": "cccccccc-cccc-cccc-cccc-cccccccccccc"
  },
  "voa_statutoryspatialunitid": {
    "entity": "voa_ssu",
    "id": "11111111-1111-1111-1111-111111111111"
  },
  "voa_readyforqualitychecks": false,
  "caseorigincode": 589160010
}
```

Team-assigned variant for job:
- Set ownerid to team reference instead of systemuser.
- Then add the created incident to the mapped team queue (AddToQueue).

## 4) Assignment fields required (user and team)

For batch submission and downstream request/job creation, capture assignment on the batch header.

Required assignment fields on Bulk Ingestion:
- voa_assignmentmode: required
- voa_assignedteam: required when mode = Team
- voa_assignedmanager: required when mode = Manager

Recommended enforcement rule:
- Exactly one assignee path is active per batch:
  - Team assignment: voa_assignedteam populated, voa_assignedmanager empty
  - User assignment: voa_assignedmanager populated, voa_assignedteam empty

Owner behavior for created Request/Job:
- If user assignment: set ownerid to the assigned user.
- If team assignment: set ownerid to the assigned team.

Queue behavior requirement for team-assigned Job:
- If ownerid is a team, the job must also be routed to that team queue.
- This is in addition to owner assignment (not instead of owner assignment).

Recommended queue-routing fields/config:
- teamId (or voa_assignedteam) as source assignment
- targetQueueId (resolved from team default queue mapping)
- queue routing result fields for audit, for example:
  - voa_jobqueued = true/false
  - voa_jobqueueid = <queue-guid>
  - voa_jobqueuedon = <utc timestamp>

## 5) Payload examples

### A. SaveItems (selection)

```json
{
  "bulkProcessorId": "00000000-0000-0000-0000-000000000001",
  "ssuIds": [
    { "statutorySpatialUnitId": "11111111-1111-1111-1111-111111111111" },
    { "statutorySpatialUnitId": "22222222-2222-2222-2222-222222222222" }
  ],
  "correlationId": "save-items-selection-001"
}
```

### B. SaveItems (file)

```json
{
  "bulkProcessorId": "00000000-0000-0000-0000-000000000001",
  "fileColumnName": "sourcefile",
  "correlationId": "save-items-file-001"
}
```

### C. SubmitBatch (user assigned)

Submit payload:

```json
{
  "bulkProcessorId": "00000000-0000-0000-0000-000000000001",
  "userId": "33333333-3333-3333-3333-333333333333",
  "componentName": "BulkSubmit",
  "requestedBy": "33333333-3333-3333-3333-333333333333",
  "correlationId": "submit-user-001"
}
```

Related required batch assignment data:

```json
{
  "voa_assignmentmode": "Manager",
  "voa_assignedmanager": "33333333-3333-3333-3333-333333333333",
  "voa_assignedteam": null
}
```

Expected creation outcome:
- Request ownerid = assigned user
- Job ownerid = assigned user
- No team queue routing required

### D. SubmitBatch (team assigned)

Submit payload:

```json
{
  "bulkProcessorId": "00000000-0000-0000-0000-000000000001",
  "userId": "44444444-4444-4444-4444-444444444444",
  "componentName": "BulkSubmit",
  "requestedBy": "44444444-4444-4444-4444-444444444444",
  "correlationId": "submit-team-001"
}
```

Related required batch assignment data:

```json
{
  "voa_assignmentmode": "Team",
  "voa_assignedteam": "55555555-5555-5555-5555-555555555555",
  "voa_assignedmanager": null,
  "voa_teamdefaultqueueid": "66666666-6666-6666-6666-666666666666"
}
```

Expected creation outcome:
- Request ownerid = assigned team
- Job ownerid = assigned team
- Job is added to team queue using voa_teamdefaultqueueid

Queue routing example (logical step after job create):

```json
{
  "jobId": "77777777-7777-7777-7777-777777777777",
  "targetQueueId": "66666666-6666-6666-6666-666666666666",
  "action": "AddToQueue",
  "requiredWhen": "assignmentmode=Team"
}
```

### E. SVT single-item request/job

```json
{
  "ssuId": "88888888-8888-8888-8888-888888888888",
  "userId": "99999999-9999-9999-9999-999999999999",
  "componentName": "SVT-UI",
  "correlationId": "svt-single-001"
}
```

## 6) Common failure reasons to expect

- INVALID_COMBINATION: mixed bulk and SVT fields, or unsupported combination.
- BULK_PROCESSOR_ID_REQUIRED: missing bulkProcessorId for bulk routes.
- INVALID_SVT_REQUEST: missing one of ssuId, userId, componentName.
- BATCH_NOT_DRAFT: batch not in Draft state.
- NO_ITEMS_TO_SUBMIT: no staged items.
- NO_VALID_ITEMS_TO_SUBMIT: no valid staged items.
- JOB_TYPE_REQUIRED: no job type available from template/header.
- INVALID_USER_FORMAT: userId is not a GUID.
- INVALID_SSU_FORMAT: SSU id is not a GUID.
- ACTIVE_REQUEST_PRESENT: duplicate active request for same SSU + job type.
- TEAM_QUEUE_REQUIRED: assignment mode is Team but no queue mapping is available.
- TEAM_QUEUE_ROUTE_FAILED: owner was set to team but add-to-queue operation failed.
