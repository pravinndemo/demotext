# Title

DevOps: Configure APIM for `ctautoprocess` Azure Function and Promote HTTP Endpoints Across Environments via Pipeline

## Description

Own the DevOps workstream to verify current APIM integration for `ctautoprocess` Azure Function and enable repeatable deployment of required HTTP-triggered endpoints to APIM for all target environments.

Scope includes:

- Review current APIM configuration for `ctautoprocess` in existing environment(s).
- Identify missing HTTP-triggered Azure Function operations that must be exposed through APIM.
- Update APIM configuration to include required endpoints (path, method, backend mapping, policies, and security settings).
- Add/update pipeline steps/templates so APIM API/operations are deployed consistently to other environments (for example: dev/test/uat/prod).
- Ensure environment-specific values are parameterized (APIM instance name, backend URL, function key/named values, policy variables).
- Validate deployment order and dependency handling in pipeline (backend first, then API/operations/policies).
- Add verification step in pipeline (smoke check or policy validation) post-deployment.
- Document required variables, secrets, and manual approvals/gates.

Expected outcome:

- `ctautoprocess` HTTP-triggered endpoints are correctly configured in APIM and automatically promoted to other environments through the DevOps pipeline with minimal manual intervention.
