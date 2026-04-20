# Dataverse setup automation

This folder contains a generated Dataverse metadata setup script for the Bulk Processor model.

## Files

- `BulkProcessorDataverseSetup.cs`
  - .NET SDK-based Dataverse metadata provisioning script.
  - Creates `voa_bulkprocessor` and `voa_bulkprocessoritem` tables if missing.
  - Creates core columns and lookup relationships.
  - Safe to rerun because it checks solution, table, column, and lookup existence before creating.
  - Targets `CTP_BulkData_Creation` by default and adds existing/new tables and columns into that solution.

- `run-sdk-setup.ps1`
  - Bootstraps a temporary .NET runner and executes `BulkProcessorDataverseSetup.cs`.

## Current user context support

Yes. This script can run in delegated current user context.

- It uses `AuthType=OAuth` with `LoginPrompt=Auto` in `ServiceClient`.
- If your Windows session already has an active token, it reuses it.
- If not, it prompts interactive sign-in for your current user.
- No separate app registration is required for this setup path.

## Prerequisites

- `pac` installed and authenticated (`pac auth list` shows an active profile)
- .NET SDK 8+
- Dataverse privileges to create/modify tables, columns, and relationships

## Run

```powershell
pwsh ./scripts/dataverse/run-sdk-setup.ps1 \
  -Url "https://orga06f2c39.crm.dynamics.com" \
  -RequestEntity "voa_request" \
  -JobEntity "voa_job"
```

Default solution settings:

- `SolutionName`: `CTP_BulkData_Creation`
- `SolutionPublisher`: `voa`
- `SolutionPrefix`: `voa`

You can override them if needed:

```powershell
pwsh ./scripts/dataverse/run-sdk-setup.ps1 \
  -Url "https://orga06f2c39.crm.dynamics.com" \
  -SolutionName "CTP_BulkData_Creation" \
  -SolutionPublisher "voa" \
  -SolutionPrefix "voa"
```

Optional flags:

- `-NoPublish` to skip `PublishAllXml`
- `-ColumnsOnly` to skip table creation and create only missing columns/lookups on existing tables
- Omit `-RequestEntity` and `-JobEntity` if those tables are not ready yet

## Notes

- Request/Job lookups are created only when the referenced table logical names exist.
- Team and Manager lookups target built-in `team` and `systemuser`.
- Status and validation values are implemented as local choice columns.
- If a table or column already exists, the script reuses it and only creates missing metadata.
