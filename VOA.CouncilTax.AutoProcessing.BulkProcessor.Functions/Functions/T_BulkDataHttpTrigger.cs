using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Functions;

public class T_BulkDataHttpTrigger
{
    private readonly BulkDataRequestProcessor _requestProcessor;

    public T_BulkDataHttpTrigger(
        ILogger<T_BulkDataHttpTrigger> logger,
        IOrganizationServiceAsync2 dataverseService)
    {
        _requestProcessor = new BulkDataRequestProcessor(logger, dataverseService);
    }

    [Function("T_BulkDataSaveItemsHttpTrigger")]
    public Task<IActionResult> RunSaveItems(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "bulk-data/save-items")] HttpRequest req)
    {
        return _requestProcessor.ProcessRequest(req, BulkRequestAction.SaveItems, svtOnly: false);
    }

    [Function("T_BulkDataSubmitBatchHttpTrigger")]
    public Task<IActionResult> RunSubmitBatch(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "bulk-data/submit-batch")] HttpRequest req)
    {
        return _requestProcessor.ProcessRequest(req, BulkRequestAction.SubmitBatch, svtOnly: false);
    }

    [Function("T_SvtSingleHttpTrigger")]
    public Task<IActionResult> RunSvtSingle(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "bulk-data/svt-single")] HttpRequest req)
    {
        return _requestProcessor.ProcessRequest(req, bulkAction: null, svtOnly: true);
    }

}
