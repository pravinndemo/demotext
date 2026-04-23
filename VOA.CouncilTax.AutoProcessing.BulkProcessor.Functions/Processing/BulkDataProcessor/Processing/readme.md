# BulkDataProcessor Module

This folder contains trigger entry points for the bulk processor module.

- `T_BulkDataHttpTrigger.cs`: HTTP endpoints for save-items, submit-batch, and svt-single.
- `T_BulkDataTimerTrigger.cs`: Scheduled processing for queued bulk ingestion.

Core implementation is organized in sibling folders:
- `Activities`: request/timer orchestration classes
- `Services`: data access and business services
- `Models`: request/response and processing models
- `Routing`: route decision builder
- `Constants`: module-specific constants
