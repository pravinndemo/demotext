using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace VOA.CouncilTax.AutoProcessing.Helpers.Utility;

public sealed class HttpCheckDynamicsConnection
{
    private readonly IOrganizationServiceAsync2 _crmService;
    private readonly ILogger<HttpCheckDynamicsConnection> _logger;

    public HttpCheckDynamicsConnection(
        IOrganizationServiceAsync2 crmService,
        ILogger<HttpCheckDynamicsConnection> logger)
    {
        _crmService = crmService;
        _logger = logger;
    }

    [Function("HttpCheckDynamicsConnection")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("HttpCheckDynamicsConnection request received.");

        try
        {
            var response = (WhoAmIResponse)_crmService.Execute(new WhoAmIRequest());

            _logger.LogInformation(
                "Dynamics connection successful. OrganizationId: {OrganizationId}, BusinessUnitId: {BusinessUnitId}, UserId: {UserId}",
                response.OrganizationId,
                response.BusinessUnitId,
                response.UserId);

            return new OkObjectResult(new
            {
                Success = true,
                Message = "Dynamics connection successful.",
                response.OrganizationId,
                response.BusinessUnitId,
                response.UserId,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dynamics connection check failed.");

            return new ObjectResult(new
            {
                Success = false,
                Message = "Dynamics connection failed.",
                Error = ex.Message,
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError,
            };
        }
    }
}
