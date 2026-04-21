# Jira Title

Enhance HTTP Azure Function to Support Flexible Input Modes (Bulk Processor + SVT)

## Jira Description

Update the existing HTTP-triggered Azure Function to support multiple input modes through a single endpoint and execute the correct processing flow based on provided parameters.

## Supported Request Modes

### Bulk Processor - Item Mode

- Input: `bulkInjectionId` + `ssuids[]`
- Behavior: Validate each `ssuid`, create `BulkInjectionItem` records, update parent `BulkInjection`, and update each `BulkInjectionItem` status.

### Bulk Processor - File Mode

- Input: `bulkInjectionId` only
- Behavior: Load/process file linked to `BulkInjection`, derive SSUIDs from file, create `BulkInjectionItem` records, update parent `BulkInjection`, and update each `BulkInjectionItem` status.

### SVT Mode

- Input: `ssuid` + `componentName`
- Behavior: SVT custom API calls Azure Function with these fields; function processes request and persists `componentName` into request/job metadata (`description` or `remarks`), then updates status accordingly.

## Flow (Implementation Order)

1. Parse request and determine mode from parameter combination.
2. Validate input contract for selected mode.
3. Validate business rules (entity existence, status eligibility, duplicate/idempotency checks).
4. Create request/job context.
5. Create `BulkInjectionItem`(s) where applicable.
6. Update `BulkInjection` aggregate fields/status where applicable.
7. Update request/job and item records with result status and remarks.
8. Ensure `componentName` is written to `description`/`remarks` in SVT mode.
9. Return standardized response and log full correlation trail.

## Validation Matrix

- `bulkInjectionId` + `ssuids[]` => Bulk Item Mode
- `bulkInjectionId` only => Bulk File Mode
- `ssuid` + `componentName` => SVT Mode
- Any other combination => `400 Bad Request` with explicit reason

## Response Expectations

- `202 Accepted` for successful acceptance/processing start
- `400 Bad Request` for invalid parameter combinations or payloads
- `404 Not Found` when target parent/request entity does not exist
- `409 Conflict` for duplicate/idempotency violations
- `500 Internal Server Error` for unhandled failures

## Acceptance Criteria

1. Function correctly routes to one of the three supported modes based on inputs.
2. Bulk Item Mode supports `bulkInjectionId + ssuids[]` and creates/updates records correctly.
3. Bulk File Mode supports `bulkInjectionId` only and processes source file to create/update items.
4. SVT Mode supports `ssuid + componentName` and persists `componentName` to request/job `description` or `remarks`.
5. Parent `BulkInjection` and child `BulkInjectionItem` updates follow defined status transitions.
6. Invalid input combinations are rejected with clear `400` errors.
7. Correlation ID, mode, ids, and status transitions are logged for auditability.
8. Unit/integration tests cover all three modes and key error paths.