# Bulk Ingestion Timer Failure KQL Runbook

This runbook is for the bulk ingestion timer path in the current codebase.

Exact names used by the function:
- Function: `T_BulkDataTimerTrigger`
- Processor: `BulkIngestionProcessor`
- Bulk ingestion entity: `voa_bulkingestion`
- Bulk ingestion item entity: `voa_bulkingestionitem`
- Processing correlation field: `ProcessingRunId`

Relevant log messages from the code:
- `T_BulkDataTimerTrigger executed at ...`
- `Queue-only bulk mode enabled: timer will create request/job records for queued bulk ingestions.`
- `No submitted ingestion records found.`
- `Processing Bulk Ingestion [...] ProcessingRunId=...`
- `Performance.TimerRunSummary ...`
- `Performance.TimerIngestionSummary ...`
- `Performance.TimerBatchSummary ...`
- `Error in BulkDataTimerTrigger`
- `Unhandled error processing BulkIngestion [...]`

## 1) Start here

This query finds the timer execution, exceptions, requests, dependencies, and trace logs for the failure window.

```kusto
let start = datetime(2026-05-07 20:45:00);
let end   = datetime(2026-05-07 21:15:00);
union isfuzzy=true traces, exceptions, requests, dependencies
| where timestamp between (start .. end)
| where tostring(message) has_any (
    "T_BulkDataTimerTrigger",
    "BulkIngestionProcessor",
    "Bulk Ingestion",
    "ProcessingRunId",
    "Performance.TimerRunSummary",
    "Performance.TimerIngestionSummary",
    "Performance.TimerBatchSummary",
    "Error in BulkDataTimerTrigger",
    "Unhandled error processing BulkIngestion",
    "No submitted ingestion records found"
)
or tostring(outerMessage) has_any (
    "T_BulkDataTimerTrigger",
    "BulkIngestionProcessor",
    "Bulk Ingestion",
    "ProcessingRunId",
    "Performance.TimerRunSummary",
    "Performance.TimerIngestionSummary",
    "Performance.TimerBatchSummary",
    "Error in BulkDataTimerTrigger",
    "Unhandled error processing BulkIngestion"
)
or tostring(name) has_any ("T_BulkDataTimerTrigger", "BulkIngestionProcessor")
| order by timestamp desc
| project timestamp, itemType=$table, operation_Id, operation_Name, cloud_RoleName, name, message, outerMessage, type, success, resultCode, duration, customDimensions
```

## 2) Timer trigger failures only

Use this when you want the function-host level error.

```kusto
let start = datetime(2026-05-07 20:45:00);
let end   = datetime(2026-05-07 21:15:00);
union isfuzzy=true requests, exceptions, traces
| where timestamp between (start .. end)
| where tostring(name) has "T_BulkDataTimerTrigger"
   or tostring(operation_Name) has "T_BulkDataTimerTrigger"
   or tostring(message) has "Error in BulkDataTimerTrigger"
   or tostring(outerMessage) has "Error in BulkDataTimerTrigger"
| order by timestamp desc
| project timestamp, itemType=$table, operation_Id, operation_Name, cloud_RoleName, name, message, outerMessage, type, success, resultCode, duration, customDimensions
```

## 3) Exceptions thrown by the processor

This is the best query for the actual root cause when the timer fails after validation.

```kusto
let start = datetime(2026-05-07 20:45:00);
let end   = datetime(2026-05-07 21:15:00);
exceptions
| where timestamp between (start .. end)
| where tostring(outerMessage) has_any (
    "Unhandled error processing BulkIngestion",
    "Error in BulkDataTimerTrigger",
    "BulkIngestionProcessor",
    "T_BulkDataTimerTrigger"
)
   or tostring(message) has_any (
    "Unhandled error processing BulkIngestion",
    "Error in BulkDataTimerTrigger",
    "BulkIngestionProcessor",
    "T_BulkDataTimerTrigger"
)
| order by timestamp desc
| project timestamp, operation_Id, operation_Name, cloud_RoleName, type, outerMessage, innermostMessage, problemId, severityLevel, customDimensions
```

## 4) Processor timeline

This shows the full sequence for the timer worker.

```kusto
let start = datetime(2026-05-07 20:45:00);
let end   = datetime(2026-05-07 21:15:00);
union isfuzzy=true traces, exceptions, requests, dependencies
| where timestamp between (start .. end)
| where tostring(message) has_any (
    "No submitted ingestion records found",
    "Processing Bulk Ingestion",
    "Performance.TimerRunSummary",
    "Performance.TimerIngestionSummary",
    "Performance.TimerBatchSummary",
    "Unhandled error processing BulkIngestion"
)
| order by timestamp asc
| project timestamp, itemType=$table, operation_Id, operation_Name, cloud_RoleName, name, message, outerMessage, success, resultCode, duration, customDimensions
```

## 5) Find the exact ingestion run

Use this after you get the `ProcessingRunId` from the logs.

```kusto
let runId = "PASTE_PROCESSING_RUN_ID_HERE";
union isfuzzy=true traces, exceptions, requests, dependencies
| where tostring(message) has runId
   or tostring(outerMessage) has runId
   or tostring(customDimensions["ProcessingRunId"]) has runId
| order by timestamp asc
| project timestamp, itemType=$table, operation_Id, operation_Name, cloud_RoleName, name, message, outerMessage, type, success, resultCode, duration, customDimensions
```

## 6) Find the exact bulk ingestion record

Use this when you know the Dataverse bulk ingestion id or name from the record.

```kusto
let bulkIngestionId = "PASTE_BULK_INGESTION_ID_OR_NAME_HERE";
union isfuzzy=true traces, exceptions, requests, dependencies
| extend biId = coalesce(
    tostring(customDimensions["IngestionId"]),
    tostring(customDimensions["BulkIngestionId"]),
    tostring(customDimensions["bulkIngestionId"]),
    tostring(customDimensions["BulkIngestionName"]),
    tostring(customDimensions["bulkIngestionName"])
)
| where biId has bulkIngestionId
   or tostring(message) has bulkIngestionId
   or tostring(outerMessage) has bulkIngestionId
| order by timestamp desc
| project timestamp, itemType=$table, operation_Id, operation_Name, cloud_RoleName, name, message, outerMessage, type, success, resultCode, duration, customDimensions
```

## 7) Failed downstream calls

Use this when the timer starts but fails in SQL, Dataverse, HTTP, or any other dependency.

```kusto
let start = datetime(2026-05-07 20:45:00);
let end   = datetime(2026-05-07 21:15:00);
dependencies
| where timestamp between (start .. end)
| where success == false or toint(resultCode) >= 400
| order by timestamp desc
| project timestamp, operation_Id, name, target, data, success, resultCode, duration, cloud_RoleName, customDimensions
```

## 8) Failed requests from the timer

```kusto
let start = datetime(2026-05-07 20:45:00);
let end   = datetime(2026-05-07 21:15:00);
requests
| where timestamp between (start .. end)
| where success == false or toint(resultCode) >= 400
| order by timestamp desc
| project timestamp, operation_Id, name, success, resultCode, duration, cloud_RoleName, operation_Name, customDimensions
```

## 9) What to look for

- `exceptions` rows before the status changes to `Failed`
- any failed `dependencies` row for Dataverse, HTTP, SQL, or storage
- a `requests` failure for `T_BulkDataTimerTrigger`
- the `ProcessingRunId` value and the last `Performance.TimerBatchSummary`
- the absence of `Performance.TimerIngestionSummary`, which usually means the function crashed before finalization

## 10) Most likely outcomes

- If you see `Error in BulkDataTimerTrigger`, the failure is at the trigger wrapper level.
- If you see `Unhandled error processing BulkIngestion [...]`, the failure is inside `BulkIngestionProcessor`.
- If you see no `Performance.TimerIngestionSummary`, the run likely died before the parent header was finalized.
- If all items are valid but nothing is captured, the failure is usually a downstream write or dependency call, not validation.
