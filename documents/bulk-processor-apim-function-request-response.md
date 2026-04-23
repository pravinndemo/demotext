# Bulk Processor APIM to Azure Function Request and Response Contract

## 1. Decision: Standard Function or Durable Function

For the current architecture, a Standard Azure Function implementation is enough.

Reasoning for this scenario:
- Input is simple and explicit: either `ssuIds` (selection) or `bulkProcessorId` (file path).
- We already separate ingestion and long-running work:
  - HTTP-triggered function for stage ingestion.
  - Timer-triggered function for background processing.
- Batch state is already persisted in Dataverse (`Bulk Processor` and `Bulk Processor Item`), so orchestration state does not need to be held by Durable runtime.

Use Durable Function later only if we add requirements such as:
- Fan-out/fan-in orchestration with strict step tracking per batch.
- External event callbacks/human interactions mid-workflow.
- Very long multi-step workflows where durable checkpoints are needed outside Dataverse state.

## 2. Flow in Scope

1. PCF calls APIM endpoint.
2. APIM forwards to Azure Function HTTP endpoint.
3. Function validates request and stages work in Dataverse.
4. Function returns immediate response (`202 Accepted` recommended).
5. `SubmitBatch` can create requests immediately and, for `Request and Job(s)`, create incidents directly in the Azure Function.
6. A bypassed follow-up request update is used so the existing plugin does not create duplicate incidents.
7. If immediate creation is disabled by configuration, queued batches can still be picked up later by a worker flow.

## 3. Scenario A: PCF Selection Path

### Endpoint
`POST /bulk-data/save-items`

### Request (APIM to Function)
```json
{
  "bulkProcessorId": "7f3c2f6a-8d1c-4d5a-9c8b-123456789abc",
  "sourceType": "PCF_SELECTION",
  "ssuIds": [
    "SSU001",
    "SSU002",
    "SSU003"
  ],
  "requestedBy": "user@contoso.com",
  "correlationId": "2d6cb2ef-f3dc-4e95-b2d8-7a4da1f55df1"
}
```

### Synchronous Response
Use `202 Accepted` because row-level processing may continue asynchronously.

```json
{
  "accepted": true,
  "bulkProcessorId": "7f3c2f6a-8d1c-4d5a-9c8b-123456789abc",
  "sourceType": "PCF_SELECTION",
  "receivedCount": 3,
  "status": "StagingAccepted",
  "message": "Selection payload accepted for staging.",
  "correlationId": "2d6cb2ef-f3dc-4e95-b2d8-7a4da1f55df1"
}
```

### Error Response Example
```json
{
  "accepted": false,
  "code": "INVALID_REQUEST",
  "message": "ssuIds is required and must contain at least 1 item.",
  "correlationId": "2d6cb2ef-f3dc-4e95-b2d8-7a4da1f55df1"
}
```

## 4. Scenario B: File Path from Dataverse File Column

This matches your latest direction: file is already stored in Dataverse (`sourcefile`), so APIM sends only `bulkProcessorId` and the function reads the file from Dataverse.

### Endpoint
`POST /bulk-data/save-items`

### Request (APIM to Function)
```json
{
  "bulkProcessorId": "7f3c2f6a-8d1c-4d5a-9c8b-123456789abc",
  "sourceType": "CSV_DATAVERSE_FILE",
  "fileColumnName": "sourcefile",
  "requestedBy": "user@contoso.com",
  "correlationId": "9f32503d-df86-4ce4-8bc0-2cfb22e902da"
}
```

### Synchronous Response
Use `202 Accepted` because file read, parse, and staging happen asynchronously.

```json
{
  "accepted": true,
  "bulkProcessorId": "7f3c2f6a-8d1c-4d5a-9c8b-123456789abc",
  "sourceType": "CSV_DATAVERSE_FILE",
  "status": "FileStagingAccepted",
  "message": "File staging request accepted. Function will read sourcefile from Dataverse.",
  "correlationId": "9f32503d-df86-4ce4-8bc0-2cfb22e902da"
}
```

### Error Response Example
```json
{
  "accepted": false,
  "code": "SOURCE_FILE_NOT_FOUND",
  "message": "No file content found in sourcefile for this bulkProcessorId.",
  "correlationId": "9f32503d-df86-4ce4-8bc0-2cfb22e902da"
}
```

## 5. Submit Processing and Optional Background Worker

### Trigger
- `POST /bulk-data/submit-batch` is the normal final action.
- Optional worker processing still applies when immediate creation is disabled.

### Work Performed
- Validates that at least one valid item exists.
- Creates `voa_requestlineitem` records for valid items.
- For `Request and Job(s)`, creates `incident` records directly in the Azure Function and links them back to both request and bulk item.
- Uses a bypassed request update when moving the request to its active status to avoid duplicate plugin execution.
- For `Request Only`, keeps the request on hold without creating an incident.
- Updates item and batch counters/status.

### Result Visibility
- `SubmitBatch` returns an immediate HTTP response.
- Status is tracked in Dataverse fields:
  - Batch status/counts on `Bulk Processor`.
  - Per-row status/error on `Bulk Processor Item`.
  - Request linkage on `Bulk Processor Item`, with job linkage potentially arriving later through downstream automation.

## 6. API Behavior Guidance

- Prefer idempotency by checking whether staging already exists for the same `bulkProcessorId` and source mode.
- Always return `correlationId` so support can trace APIM + Function + Dataverse logs.
- Keep HTTP request lightweight.
- Keep staging in `save-items` and perform direct incident creation only in the final submit path.

## 7. Recommendation Summary

- Recommended now: Standard Azure Functions with HTTP submit plus optional queued worker behavior.
- Not required now: Durable Functions.
- Revisit Durable only if orchestration complexity grows beyond Dataverse-backed batch state.
