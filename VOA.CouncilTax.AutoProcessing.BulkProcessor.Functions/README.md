# VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions

Azure Functions app with two triggers:
- `BulkDataHttpTrigger`
- `BulkDataTimerTrigger`

## Run locally

```powershell
dotnet restore
dotnet build
func start
```

HTTP endpoint:
- `GET/POST http://localhost:7071/api/bulk-data/process`

Timer schedule:
- Every 5 minutes (`0 */5 * * * *`).

---

## Configurable chunking and retry environment variables

The following environment variables control how bulk processing is chunked and retried. All are optional — safe defaults are used when they are absent.

### HTTP SaveItems (`BulkDataRequestProcessor.HandleSaveItemsAsync`)

These settings apply to both BULK_SELECTION (SSU IDs payload) and BULK_FILE (CSV) flows when staging items.

| Environment variable | Default | Range | Description |
|---|---|---|---|
| `BulkSaveItemsExecuteMultipleChunkSize` | `100` | 1–1000 | Number of `voa_UpsertHereditamentLinkV1` requests per `ExecuteMultiple` chunk call. |
| `BulkSaveItemsItemUpsertChunkSize` | `100` | 1–1000 | Number of bulk ingestion item create/update requests per `ExecuteMultiple` chunk call. |
| `BulkSaveItemsMaxRetries` | `3` | 0–10 | Maximum retry attempts per chunk on transient failure (exponential back-off). |
| `BulkSaveItemsBaseDelayMs` | `500` | 50–30000 | Base delay in milliseconds for exponential back-off (`delay = base * 2^(attempt-1)`). |

**Graceful partial-success behaviour:**
- If some chunks succeed and some fail, the request returns `202 Accepted` with a `[PartialWriteFailure]` warning in the response message rather than failing entirely.
- The request returns `500` only when **all** chunks fail.
- Invalid or out-of-range config values are logged as warnings and replaced with the safe default.

### Timer-driven processing (`BulkIngestionProcessor`)

| Environment variable | Default | Range | Description |
|---|---|---|---|
| `BulkTimerBatchSize` | `200` | 1–5000 | Number of valid ingestion items processed per timer batch. Replaces the former hardcoded constant of `1000`. |
| `BulkSingleItemRetryMaxConcurrency` | `10` | 1–100 | Maximum number of single-item retries that run concurrently within a batch (via `SemaphoreSlim`). Avoids spiking Dataverse with hundreds of concurrent retries. |

---

## Testing the chunking behaviour

The chunk logic lives in small helper methods that can be validated in isolation:

- **`GetIntConfigValue` (both processors)** — pure functions; test by setting environment variables and asserting return values for valid, missing, unparseable, and out-of-range inputs.
- **`ExecuteMultipleInChunksAsync`** — inject a mock `IOrganizationServiceAsync2` that throws on the first call and succeeds on the second to verify retry behaviour. Assert the returned `(SucceededChunks, FailedChunks, Errors)` tuple.
- **`RetryWithBackoffAsync`** — inject a delegate that throws `n` times then succeeds; verify it retries exactly `n` times and that the final result is returned.
- **BatchSize property** — set `BulkTimerBatchSize` to a value and assert `BatchSize` returns it; unset it and assert the default `200` is returned.

