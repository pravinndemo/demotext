# Title
Create Azure Function to orchestrate bulk processor file handling and call Custom APIs

# Description
Build the Azure Function that processes uploaded bulk files after they are stored and uses the existing configuration and service connection details already used by the other teams.

The Azure Function should act as the orchestration layer for the bulk processor flow and coordinate with Dataverse through Custom APIs.

Scope:
- Reuse the existing config, secrets, and service connection pattern already used by the other teams.
- Read the uploaded file from the selected storage location.
- Parse and validate the file content.
- Call the appropriate Dataverse Custom APIs.
- Update `Bulk Processor` and `Bulk Processor Item` records as processing progresses.
- Handle batch completion, failures, and summary updates.

Key expectations:
- Do not create a separate or incompatible connection pattern if an existing one is already available.
- Keep the Azure Function focused on orchestration and processing.
- Keep business operations inside Custom APIs where appropriate.
- Ensure the flow supports item staging and downstream updates in Dataverse.

Deliverables:
- Azure Function implementation.
- Custom API invocation from the function.
- Configuration aligned with the existing team setup.
- Logging and error handling for file processing and Dataverse updates.

Acceptance criteria:
- The function can be triggered and reads the uploaded file successfully.
- The function calls the required Custom API endpoints using the shared configuration/service connection setup.
- `Bulk Processor` and `Bulk Processor Item` are updated correctly.
- Failures are logged and handled without breaking the overall processing flow.
