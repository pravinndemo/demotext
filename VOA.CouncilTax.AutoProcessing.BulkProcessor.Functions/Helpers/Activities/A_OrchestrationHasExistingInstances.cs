using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace VOA.CouncilTax.AutoProcessing.Helpers.Activities;

public sealed class OrchestrationHasExistingInstancesRequest
{
    public string InstanceId { get; set; } = string.Empty;

    public string? OrchestrationName { get; set; }
}

public sealed class A_OrchestrationHasExistingInstances
{
    private readonly ILogger<A_OrchestrationHasExistingInstances> _logger;

    public A_OrchestrationHasExistingInstances(ILogger<A_OrchestrationHasExistingInstances> logger)
    {
        _logger = logger;
    }

    [Function("A_OrchestrationHasExistingInstances")]
    public Task<bool> Run([ActivityTrigger] OrchestrationHasExistingInstancesRequest request)
    {
        if (request is null)
        {
            _logger.LogWarning("A_OrchestrationHasExistingInstances called with null request.");
            return Task.FromResult(false);
        }

        _logger.LogInformation("A_OrchestrationHasExistingInstances evaluated for InstanceId: {InstanceId}", request.InstanceId);
        return Task.FromResult(!string.IsNullOrWhiteSpace(request.InstanceId));
    }
}
