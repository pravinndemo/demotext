# Title

Implement Dataverse + Model-Driven App + PCF Enhancements for Bulk Ingestion and Azure Function Integration

## Description

Own the third workstream for bulk ingestion by implementing Dataverse metadata changes, app UI updates, PCF enhancements, and custom API integration.

Scope includes:

- Create/update Dataverse tables required for bulk ingestion processing.
- Create/update required columns (including required fields, status/remarks/mapping columns) and apply correct data types and constraints.
- Implement field and table mappings needed by bulk processing flow.
- Update existing Council Tax sitemap to expose new/updated bulk ingestion areas.
- Update Bulk Ingestion model-driven app components:
  - Main form updates
  - Views updates
  - Required field behavior
  - Business rules for conditional validation and field logic
- Implement/enhance PCF upload file control for bulk ingestion.
- Add/extend custom API that passes requests to Azure Function.
- Update PCF to include basic client-side validations before submit (for example: required fields, file type, empty file, max size, duplicate/basic format checks).
- Ensure request metadata (such as component/remarks/description where applicable) is captured and persisted consistently.

Expected outcome:

- End-to-end metadata/UI/control readiness for bulk ingestion, with validated input at UI layer and successful handoff to Azure Function via custom API.
