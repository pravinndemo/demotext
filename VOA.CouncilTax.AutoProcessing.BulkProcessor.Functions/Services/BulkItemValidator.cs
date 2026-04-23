using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Services;

/// <summary>
/// Service to validate bulk ingestion items after creation/update.
/// Performs staging-level validation: format checks, duplicate detection, required fields.
/// </summary>
public sealed class BulkItemValidator
{
    private readonly IOrganizationServiceAsync2 _dataverseService;
    private readonly ILogger _logger;

    public BulkItemValidator(IOrganizationServiceAsync2 dataverseService, ILogger logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
    }

    /// <summary>
    /// Validates all items in a batch and updates their validation status based on rules.
    /// Returns updated count breakdown (valid, invalid, duplicate).
    /// </summary>
    public async Task<ValidationResult> ValidateBatchItemsAsync(
        Guid bulkProcessorId,
        string bulkIngestionItemEntityName,
        string bulkIngestionItemParentLookupColumnName)
    {
        var result = new ValidationResult();

        try
        {
            _logger.LogInformation(
                "Starting item validation for batch {BulkProcessorId}",
                bulkProcessorId);

            // Query all items in this batch
            var query = new QueryExpression(bulkIngestionItemEntityName)
            {
                ColumnSet = new ColumnSet(true),
                Criteria = new FilterExpression()
                {
                    Conditions =
                    {
                        new ConditionExpression(bulkIngestionItemParentLookupColumnName, ConditionOperator.Equal, bulkProcessorId),
                    }
                }
            };

            var allItems = await _dataverseService.RetrieveMultipleAsync(query);

            // Track SSU IDs and source values to detect duplicates
            var seenSsuIds = new HashSet<string>();
            var seenSourceValues = new HashSet<string>();
            var updateRequests = new List<OrganizationRequest>();

            var validationStatusColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemValidationStatusColumnName") ?? "voa_validationstatus";
            var validationMessageColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemValidationMessageColumnName") ?? "voa_validationmessage";
            var isDuplicateColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemIsDuplicateColumnName") ?? "voa_isduplicate";
            var duplicateCategoryColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemDuplicateCategoryColumnName") ?? "voa_duplicatecategory";
            var ssuIdColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemSSUIdColumnName") ?? "voa_ssuid";
            var sourceValueColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemSourceValueColumnName") ?? "voa_sourcevalue";

            foreach (var item in allItems.Entities)
            {
                var itemId = item.Id;
                var ssuId = item.GetAttributeValue<string>(ssuIdColumnName) ?? string.Empty;
                var sourceValue = item.GetAttributeValue<string>(sourceValueColumnName) ?? string.Empty;
                var validationStatus = "Valid";
                var validationMessage = "";
                var isDuplicate = false;
                var duplicateCategory = "";

                // Rule 1: Source Value must be present
                if (string.IsNullOrWhiteSpace(sourceValue))
                {
                    validationStatus = "Invalid";
                    validationMessage = "Source Value is required.";
                    result.InvalidCount++;
                }
                // Rule 2: Check for duplicates within this batch
                else if (!seenSsuIds.Add(ssuId))
                {
                    validationStatus = "Invalid";
                    validationMessage = "Duplicate SSU ID within this batch.";
                    isDuplicate = true;
                    duplicateCategory = "Same Batch";
                    result.DuplicateCount++;
                }
                // Rule 3: Check for duplicate source values in same batch (if applicable)
                else if (!string.IsNullOrWhiteSpace(sourceValue) && !seenSourceValues.Add(sourceValue))
                {
                    validationStatus = "Invalid";
                    validationMessage = "Duplicate Source Value within this batch.";
                    isDuplicate = true;
                    duplicateCategory = "Same Batch";
                    result.DuplicateCount++;
                }
                else
                {
                    result.ValidCount++;
                }

                // Build update request
                var updateEntity = new Entity(bulkIngestionItemEntityName, itemId)
                {
                    [validationStatusColumnName] = validationStatus,
                    [validationMessageColumnName] = validationMessage,
                    [isDuplicateColumnName] = isDuplicate,
                };

                if (isDuplicate && !string.IsNullOrWhiteSpace(duplicateCategory))
                {
                    updateEntity[duplicateCategoryColumnName] = duplicateCategory;
                }

                updateRequests.Add(new Microsoft.Xrm.Sdk.Messages.UpdateRequest { Target = updateEntity });

                _logger.LogInformation(
                    "Item {ItemId}: status={Status}, duplicate={IsDuplicate}, message={Message}",
                    itemId, validationStatus, isDuplicate, validationMessage);
            }

            // Execute batch updates
            if (updateRequests.Count > 0)
            {
                var writer = new DataverseBulkItemWriter(_dataverseService);
                var writeResult = writer.ExecuteItemRequests(updateRequests);

                _logger.LogInformation(
                    "Validation updates: {SuccessCount} succeeded, {FailureCount} failed",
                    writeResult.SucceededOperationCount,
                    writeResult.FailedOperationCount);

                result.UpdatedCount = writeResult.SucceededOperationCount;
            }

            result.TotalCount = allItems.Entities.Count;
            result.InvalidCount = result.TotalCount - result.ValidCount - result.DuplicateCount;

            _logger.LogInformation(
                "Validation complete for batch {BulkProcessorId}: Valid={ValidCount}, Invalid={InvalidCount}, Duplicate={DuplicateCount}",
                bulkProcessorId, result.ValidCount, result.InvalidCount, result.DuplicateCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating items for batch {BulkProcessorId}", bulkProcessorId);
            throw;
        }

        return result;
    }

    /// <summary>
    /// Checks if an SSU ID already exists in other batches (for duplicate detection across batches).
    /// </summary>
    public async Task<bool> SsuIdExistsInOtherBatchesAsync(
        Guid bulkProcessorId,
        string ssuId,
        string bulkIngestionItemEntityName,
        string bulkIngestionItemParentLookupColumnName,
        string ssuIdColumnName)
    {
        try
        {
            var query = new QueryExpression(bulkIngestionItemEntityName)
            {
                ColumnSet = new ColumnSet(false),
                PageInfo = new PagingInfo { PageNumber = 1, Count = 1 },
                Criteria = new FilterExpression()
                {
                    Conditions =
                    {
                        new ConditionExpression(ssuIdColumnName, ConditionOperator.Equal, ssuId),
                        new ConditionExpression(bulkIngestionItemParentLookupColumnName, ConditionOperator.NotEqual, bulkProcessorId),
                    }
                }
            };

            var result = await _dataverseService.RetrieveMultipleAsync(query);
            return result.Entities.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if SSU ID {SsuId} exists in other batches", ssuId);
            return false;
        }
    }
}

/// <summary>
/// Result summary from batch item validation.
/// </summary>
public sealed class ValidationResult
{
    public int TotalCount { get; set; }

    public int ValidCount { get; set; }

    public int InvalidCount { get; set; }

    public int DuplicateCount { get; set; }

    public int UpdatedCount { get; set; }
}
