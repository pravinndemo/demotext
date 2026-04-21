# Title

Set Up Manual APIM Configuration for WelshPoC and Document Required Changes

## Description

Own the APIM workstream for `welshpoc` and configure it manually to support the new bulk processing integration.

This task is focused on understanding the current APIM setup, identifying the exact deltas required for `welshpoc`, applying configuration changes manually, and documenting everything for repeatable rollout.

Scope includes:

- Review existing APIM configuration (products, APIs, operations, policies, named values, backends).
- Identify all required changes for `welshpoc` environment to support function endpoints.
- Manually configure/update APIM components for `welshpoc`:
  - API and operations
  - Backend and routing
  - Authentication/authorization settings
  - Required policies (for example: inbound validation, headers, rewrite, rate limit, retry, error handling)
  - Named values/secrets references
- Ensure request/response contract alignment between APIM and Azure Function.
- Validate end-to-end call path from client/custom API through APIM to Azure Function.
- Produce a configuration change log and deployment notes for future environments.

Expected outcome:

- `welshpoc` APIM is manually configured and verified for bulk processing flows, and a clear implementation document exists listing all required changes, rationale, and validation evidence.
