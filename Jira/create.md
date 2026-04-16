# Title
Explore file storage options for PCF bulk processor upload

# Description
We have two Dataverse entities in scope: `Bulk Processor` and `Bulk Processor Item`.

After the user uploads a file through the PCF, the team should assess where the file should be stored and how the downstream processing should start.

Focus areas to explore:
- Best storage option for the uploaded file.
- How the stored file can trigger Azure Function or Custom API processing.
- How the processing step updates `Bulk Processor` and `Bulk Processor Item`.
- Security, size, and operational constraints for each storage option.
- Whether the upload flow should be direct from PCF to storage or staged through Dataverse first.

Options to explore:
1. SharePoint
   - Good for document-style storage and user-facing file management.
   - Can act as the source for Azure Function or Custom API processing.
   - Needs review for permissions, integration pattern, and metadata handling.

2. Azure Blob Storage
   - Good for scalable file storage and backend processing.
   - Works well when Azure Function needs to pick up the file for parsing and staging.
   - Needs review for access control, retention, and file naming conventions.

3. Dataverse
   - Simple from an application perspective because the data stays in the platform.
   - Not preferred for file storage at scale because of size limits, security constraints, and Dataverse storage capacity concerns.
   - Should only be considered if the file payload is very small and the usage is tightly controlled.

Expected output from the team:
- Recommended storage option for the file upload flow.
- Reasoned comparison of SharePoint vs Blob vs Dataverse.
- Why Dataverse should or should not be used for file storage.
- The preferred trigger pattern for Azure Function or Custom API.
- The update flow for `Bulk Processor` and `Bulk Processor Item` after processing.
