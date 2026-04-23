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
