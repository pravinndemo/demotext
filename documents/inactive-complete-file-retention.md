# Inactive Complete Record File Retention

## Policy
If the main item (parent record) becomes `Inactive` because processing is `Complete`, clear the `sourcefile` file column after 15 days.

## Objective
- Reduce Dataverse file capacity usage.
- Keep the business record and processing history.
- Remove only the uploaded source file content.

## Recommended Implementation
Use a timer-triggered Azure Function that runs daily.

## Functional Rule
A record is eligible when all conditions are true:
- Main item status is `Inactive`.
- Completion state is `Complete`.
- `CompletedOn` (or equivalent completion timestamp) is older than 15 days.
- `sourcefile` is not null.

Action:
- Set `sourcefile` to null (clear file only).
- Optionally set metadata fields such as:
  - `SourceFileClearedOn` = current UTC timestamp
  - `SourceFileClearReason` = `InactiveCompleteRetention15Days`

## High-Level Flow
1. Timer-triggered Azure Function starts on schedule (for example, once per day).
2. Function authenticates to Dataverse with managed identity or app registration.
3. Function queries eligible records.
4. Function updates each eligible record to clear `sourcefile`.
5. Function logs counts: scanned, matched, cleared, failed.

## Operational Notes
- Process in batches to avoid long-running executions.
- Include retry handling for transient Dataverse/API failures.
- Add idempotency checks so reruns are safe.
- Keep audit logging for support and compliance.

## Non-Functional Guardrails
- Do not delete the Dataverse record.
- Do not clear files for records that are still active or not complete.
- Keep least-privilege Dataverse permissions for the function identity.
