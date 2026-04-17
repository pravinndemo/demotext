# File Upload Options Summary

Team,

I reviewed the file upload options we discussed for the bulk processor flow and I’ve pulled together the practical choices and the limitations for each one.

## Context

- App type: model-driven app
- Upload entry point: PCF inside the app
- File type: CSV
- Typical size we are expecting: about 100 to 5,000 rows and around 10 columns

## Options we explored

### 1. PCF + Custom API

How it works:
- The PCF reads the file and sends it to a Dataverse Custom API.
- The Custom API or plugin handles the upload flow and records the file reference back in Dataverse.

Why it works:
- It is the most Dataverse-native option.
- It gives us a clean entry point from the model-driven app.
- It is straightforward to tie the upload to the `Bulk Processor` record and the business rules.

Limitations:
- The file still travels through the Dataverse request.
- Base64 adds overhead.
- It is fine for small files, but it becomes less attractive as file size grows.

### 2. PCF + Custom API + Azure Function

How it works:
- The PCF calls a Dataverse Custom API.
- The Custom API delegates the upload and processing work to an Azure Function or upload service.
- The Azure Function reads the file content, performs basic validation checks, creates `Bulk Processor Item` rows, updates `Bulk Processor` status, and stores the file in SharePoint or Blob.
- Dataverse stores the file reference and the processing metadata.

Why it works:
- This gives us the best balance of Dataverse control and proper backend upload handling.
- It keeps the model-driven app as the entry point.
- It is the most future-safe approach if the file size or complexity increases later.

Limitations:
- It has a few more moving parts than the simple MVP route.
- We need to make sure the authentication and endpoint design are handled properly.
- The Azure Function logic has to be designed carefully so file handling, validation, and Dataverse updates stay consistent.

### 3. PCF + Azure Function upload

How it works:
- The PCF sends the file directly to an Azure Function.
- The Azure Function stores the file in SharePoint or Blob.
- Dataverse is updated separately with the file reference and status.

Why it works:
- It gives a clean file transfer path.
- It is better if we expect larger files later.
- It avoids pushing the full file through Dataverse.

Limitations:
- It is less Dataverse-centric.
- The browser-to-backend design means auth and CORS need proper attention.

### 4. Power Apps + Flow

How it works:
- The upload is handled through Power Apps and Power Automate.
- Flow stores and processes the file.

Why it works:
- It is low-code.
- It is familiar to business teams.

Limitations:
- It adds another path outside the core model-driven app experience.
- It has the same payload and transfer concerns.
- It introduces connector overhead and throttling considerations.

### 5. Dataverse notes / attachment storage

How it works:
- The file lands in a Dataverse note or attachment first.
- A plugin or downstream process moves it onward later.

Why it works:
- It is the most Dataverse-native from a storage perspective.

Limitations:
- It creates an extra storage hop.
- It can duplicate data.
- It is not a good ingestion pattern if the file is only being used as a processing input.
- I would only use this if we explicitly want Dataverse to hold the file first.

## Practical limitations

### File size
- Dataverse single request payload ceiling is 128 MB.
- That is an absolute limit, not a design target.
- For a base64-in-request pattern:
  - suitable for MVP: up to about 5 MB raw CSV
  - still workable with care: up to about 10 MB raw CSV
  - beyond that: move to backend upload or chunked upload

### Row count
- There is no hard row limit in the CSV itself.
- The practical limit is driven by file size, validation complexity, and processing time.
- 100 to 5,000 rows is a safe and modest range for this design.
- If we start moving toward 20,000+ rows, we should revisit the upload pattern.

### Column count
- There is no hard platform column limit for this pattern.
- With around 10 columns, this is still small and manageable.
- Even 20 to 30 columns can still be workable if the cell values stay short.
- The bigger concern is long text per cell, not the number of columns alone.

## Can this handle 100 to 5,000 items?

Yes, this pattern can handle that range if we treat Azure Function as the processing engine and keep Dataverse writes batched.

What needs to happen:
- Azure Function reads the file content.
- Azure Function performs the basic validation checks.
- Azure Function creates `Bulk Processor Item` rows in batches.
- Azure Function updates `Bulk Processor` status and counts.
- Azure Function stores the file in SharePoint or Blob.

What to avoid:
- Do not process all rows synchronously inside the Custom API or plugin.
- Do not create or update 100 to 5,000 Dataverse rows one by one if batching is possible.
- Do not keep the file in Dataverse as the processing store.

Practical Dataverse limits to keep in mind:
- `ExecuteMultiple` batch size is typically up to 1,000 operations.
- Dataverse service protection limits still apply for request volume and execution time.
- Synchronous Dataverse operations have a hard time limit, so the long-running work should stay outside the sync request path.

## Decision Table

| Option | How it works | Pros | Limits / concerns | My view |
|---|---|---|---|---|
| PCF + Custom API | PCF sends the file into Dataverse through a Custom API | Most Dataverse-native; easy to connect to `Bulk Processor`; simple MVP shape | File travels through Dataverse; base64 overhead; less attractive as files grow | Good MVP option |
| PCF + Custom API + Azure Function | PCF calls Custom API, then Azure Function reads the file, validates it, creates `Bulk Processor Item` rows, updates `Bulk Processor`, and stores the file in SharePoint or Blob | Best balance of governance and backend processing; clean separation; future-safe | More moving parts; needs proper auth, validation, and Dataverse update handling | Backend processing option |
| PCF + Azure Function upload | PCF uploads directly to Azure Function, which stores the file in SharePoint or Blob | Clean file transfer path; good for larger files; avoids pushing full file through Dataverse | Less Dataverse-centric; CORS/auth need care | Backend upload option |
| Power Apps + Flow | Power Apps/Flow handles upload and storage | Low-code; familiar to business teams | Extra path outside the model-driven app; throttling and connector overhead | Workable, but adds extra overhead |
| Dataverse notes / attachment storage | File lands in Dataverse first, then is moved later | Most Dataverse-native for storage | Extra storage hop; duplication; not ideal for ingestion-only files | Avoid unless required |
