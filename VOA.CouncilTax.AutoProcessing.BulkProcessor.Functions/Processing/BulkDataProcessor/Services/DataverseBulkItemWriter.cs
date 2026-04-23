using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Services;

public sealed class DataverseBulkItemWriter
{
    private const int DefaultBatchSize = 100;

    private readonly IOrganizationServiceAsync2 _dataverseService;

    public DataverseBulkItemWriter(IOrganizationServiceAsync2 dataverseService)
    {
        _dataverseService = dataverseService;
    }

    public BulkItemWriteResult ExecuteItemRequests(IEnumerable<OrganizationRequest> requests, int batchSize = DefaultBatchSize)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var requestList = requests.Where(request => request is not null).ToList();
        var result = new BulkItemWriteResult
        {
            RequestedOperationCount = requestList.Count,
        };

        if (requestList.Count == 0)
        {
            return result;
        }

        foreach (var chunk in ChunkRequests(requestList, batchSize))
        {
            var executeMultipleRequest = new ExecuteMultipleRequest
            {
                Settings = new ExecuteMultipleSettings
                {
                    ContinueOnError = true,
                    ReturnResponses = true,
                },
                Requests = new OrganizationRequestCollection(),
            };

            foreach (var request in chunk)
            {
                executeMultipleRequest.Requests.Add(request);
            }

            var response = (ExecuteMultipleResponse)_dataverseService.Execute(executeMultipleRequest);
            MergeBatchResult(chunk.Count, response, result);
        }

        return result;
    }

    public void UpdateBatchCounters(Guid bulkProcessorId, BulkItemCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var bulkProcessorEntityName = Environment.GetEnvironmentVariable("BulkProcessorEntityLogicalName") ?? "voa_bulkingestion";

        var totalRowsColumnName = Environment.GetEnvironmentVariable("BulkProcessorTotalRowsColumnName") ?? "voa_totalrows";
        var validItemCountColumnName = Environment.GetEnvironmentVariable("BulkProcessorValidItemCountColumnName") ?? "voa_validitemcount";
        var invalidItemCountColumnName = Environment.GetEnvironmentVariable("BulkProcessorInvalidItemCountColumnName") ?? "voa_invaliditemcount";
        var duplicateItemCountColumnName = Environment.GetEnvironmentVariable("BulkProcessorDuplicateItemCountColumnName") ?? "voa_duplicateitemcount";
        var processedItemCountColumnName = Environment.GetEnvironmentVariable("BulkProcessorProcessedItemCountColumnName") ?? "voa_processeditemcount";
        var failedItemCountColumnName = Environment.GetEnvironmentVariable("BulkProcessorFailedItemCountColumnName") ?? "voa_faileditemcount";

        var updateEntity = new Entity(bulkProcessorEntityName, bulkProcessorId)
        {
            [totalRowsColumnName] = counts.TotalRows,
            [validItemCountColumnName] = counts.ValidItemCount,
            [invalidItemCountColumnName] = counts.InvalidItemCount,
            [duplicateItemCountColumnName] = counts.DuplicateItemCount,
            [processedItemCountColumnName] = counts.ProcessedItemCount,
            [failedItemCountColumnName] = counts.FailedItemCount,
        };

        _dataverseService.Update(updateEntity);
    }

    public static OrganizationRequest BuildUpsertRequest(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.Id == Guid.Empty)
        {
            return new CreateRequest { Target = entity };
        }

        return new UpdateRequest { Target = entity };
    }

    public static OrganizationRequest BuildDeleteRequest(string entityLogicalName, Guid entityId)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            throw new ArgumentException("Entity logical name is required.", nameof(entityLogicalName));
        }

        if (entityId == Guid.Empty)
        {
            throw new ArgumentException("Entity id is required.", nameof(entityId));
        }

        return new DeleteRequest
        {
            Target = new EntityReference(entityLogicalName, entityId),
        };
    }

    private static IEnumerable<List<OrganizationRequest>> ChunkRequests(List<OrganizationRequest> requests, int batchSize)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        for (var index = 0; index < requests.Count; index += batchSize)
        {
            yield return requests.Skip(index).Take(batchSize).ToList();
        }
    }

    private static void MergeBatchResult(int requestedCount, ExecuteMultipleResponse response, BulkItemWriteResult result)
    {
        var failureIndices = new HashSet<int>();

        foreach (var itemResponse in response.Responses)
        {
            if (itemResponse.Fault is null)
            {
                continue;
            }

            failureIndices.Add(itemResponse.RequestIndex);
            result.Errors.Add($"Request index {itemResponse.RequestIndex} failed: {itemResponse.Fault.Message}");
        }

        result.FailedOperationCount += failureIndices.Count;
        result.SucceededOperationCount += requestedCount - failureIndices.Count;
    }
}

