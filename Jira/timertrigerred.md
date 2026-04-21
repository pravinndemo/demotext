# Jira Title

Implement Timer-Triggered Azure Function for Bulk Injection Processing

## Jira Description

Create a timer-triggered Azure Function to pick up pending bulk injection work, process records in batches, and keep `BulkInjection` and `BulkInjectionItem` statuses in sync.

The timer function is responsible for background execution and retry-safe progression of work created by upstream APIs (HTTP/custom API).

## Trigger and Schedule

- Trigger type: Azure Functions Timer Trigger
- Schedule: configurable via app setting (for example every 5 minutes)
- Use `WEBSITE_TIME_ZONE`/UTC strategy as per environment standard
- Prevent overlapping runs (lock/lease strategy)

## Processing Scope

- Process parent records from `BulkInjection` in eligible states (for example `Submitted`, `Queued`, `InProgress-Retry`)
- For each parent, process related `BulkInjectionItem` records in eligible states
- Support file-driven bulk injection flow where items are derived from uploaded file if needed

## Flow (Implementation Order)

1. Timer fires based on configured CRON schedule.
2. Acquire execution lock to avoid parallel duplicate runs.
3. Read eligible `BulkInjection` records (batch + pagination).
4. For each `BulkInjection`:
   - Validate parent is still in processable state.
   - If file mode and items are missing, derive/create `BulkInjectionItem` records from file content.
   - Fetch pending/failed-retry `BulkInjectionItem` records.
5. For each `BulkInjectionItem`:
   - Mark item as `Processing`.
   - Execute business processing for `ssuid`.
   - Update item status to `Completed` or `Failed` with error remarks.
6. Update parent `BulkInjection` aggregate values:
   - total/processed/success/failed counts
   - parent status (`InProgress`, `PartiallyCompleted`, `Completed`, `Failed`)
7. Persist run summary and telemetry (counts, timings, correlation ids, error categories).
8. Release lock and end run.

## Retry and Resilience Rules

- Idempotent processing per `BulkInjectionItem` (no duplicate completion updates).
- Retry failed items up to configured max retry count.
- Move permanently failed items to terminal status with final error reason.
- Continue processing remaining items even if one item fails.
- Support safe re-run of timer without data corruption.

## Response/Execution Expectations

- No HTTP response (background trigger).
- Execution result should be observable through logs + Dataverse statuses.
- Emit structured logs for run id, parent id, item id, status transitions, and failures.

## Acceptance Criteria

1. Timer function runs on configured schedule and does not overlap with itself.
2. Eligible `BulkInjection` records are picked and processed in batches.
3. `BulkInjectionItem` records are created from file when required by bulk file mode.
4. Each item transitions through valid statuses and captures failure remarks when applicable.
5. Parent `BulkInjection` status and counts are updated correctly after item processing.
6. Retry policy is enforced and terminal failure is handled deterministically.
7. Function is idempotent for re-runs and partial-failure recovery.
8. Structured telemetry/logging is available for audit and troubleshooting.
9. Unit/integration tests cover happy path, partial failure, retry, and re-run scenarios.

## Definition of Done

1. Code implemented and merged with review approval.
2. Schedule and batch/retry settings are configurable via app settings.
3. Test coverage added for orchestration and status transitions.
4. Operational runbook updated with trigger schedule, failure handling, and rerun guidance.
