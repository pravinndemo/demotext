using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Services;

public static class CrossBatchDuplicateLookupService
{
    public static async Task<List<CrossBatchDuplicateBatchInfo>> FindConflictingBatchesAsync(
        IOrganizationServiceAsync2 dataverseService,
        ILogger logger,
        Guid currentBatchId,
        string ssuId,
        string itemEntityName,
        string itemParentLookupColumnName,
        string ssuIdColumnName,
        string batchEntityName)
    {
        var query = new QueryExpression(itemEntityName)
        {
            ColumnSet = new ColumnSet(itemParentLookupColumnName),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 },
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression(ssuIdColumnName, ConditionOperator.Equal, ssuId),
                    new ConditionExpression(itemParentLookupColumnName, ConditionOperator.NotEqual, currentBatchId),
                }
            }
        };

        var conflictBatchIds = new HashSet<Guid>();
        do
        {
            var result = await dataverseService.RetrieveMultipleAsync(query);
            foreach (var entity in result.Entities)
            {
                var parentBatch = entity.GetAttributeValue<EntityReference>(itemParentLookupColumnName);
                if (parentBatch is null || parentBatch.Id == Guid.Empty || parentBatch.Id == currentBatchId)
                {
                    continue;
                }

                conflictBatchIds.Add(parentBatch.Id);
            }

            if (!result.MoreRecords)
            {
                break;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = result.PagingCookie;
        } while (true);

        var conflicts = new List<CrossBatchDuplicateBatchInfo>(conflictBatchIds.Count);
        foreach (var conflictBatchId in conflictBatchIds)
        {
            string batchReference = string.Empty;

            try
            {
                var batch = await dataverseService.RetrieveAsync(
                    batchEntityName,
                    conflictBatchId,
                    new ColumnSet(EntityFields.BulkIngestionFields.Name));
                batchReference = batch.GetAttributeValue<string>(EntityFields.BulkIngestionFields.Name)?.Trim() ?? string.Empty;
            }
            catch (Exception lookupEx)
            {
                logger.LogWarning(
                    lookupEx,
                    "Unable to resolve batch name for duplicate SSU {SsuId} in batch {BatchId}",
                    ssuId,
                    conflictBatchId);
            }

            conflicts.Add(new CrossBatchDuplicateBatchInfo
            {
                BatchId = conflictBatchId,
                BatchName = batchReference,
            });
        }

        return conflicts;
    }
}
