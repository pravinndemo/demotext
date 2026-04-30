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

/// <summary>
/// Service to validate bulk ingestion items after creation/update.
/// Performs staging-level validation: SSUID checks, duplicate detection, and optional source value requirements.
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

            // Track normalized SSU IDs and source values to detect duplicates
            var seenSsuIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenSourceValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var updateRequests = new List<OrganizationRequest>();
            var validationErrors = new List<string>();

            var validationStatusColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemValidationStatusColumnName") ?? "voa_validationstatus";
            var validationMessageColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemValidationMessageColumnName") ?? "voa_validationfailurereason";
            var isDuplicateColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemIsDuplicateColumnName") ?? "voa_isduplicate";
            var duplicateCategoryColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemDuplicateCategoryColumnName") ?? "voa_duplicatecategory";
            var ssuIdColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemSSUIdColumnName") ?? "voa_ssuid";
            var sourceValueColumnName = Environment.GetEnvironmentVariable("BulkIngestionItemSourceValueColumnName") ?? "voa_sourcevalue";
            var requireSourceValue = GetBooleanFlag("BulkIngestionItemRequireSourceValue", false);
            var checkCrossBatchDuplicates = GetBooleanFlag("BulkIngestionCheckCrossBatchDuplicates", false);

            foreach (var item in allItems.Entities)
            {
                var itemId = item.Id;
                var rawSsuId = item.GetAttributeValue<string>(ssuIdColumnName) ?? string.Empty;
                var rawSourceValue = item.GetAttributeValue<string>(sourceValueColumnName) ?? string.Empty;
                var ssuId = NormalizeValue(rawSsuId);
                var sourceValue = NormalizeValue(rawSourceValue);
                var validationStatus = StatusCodes.Valid;
                var validationMessage = "";
                var isDuplicate = false;
                //var duplicateCategory = "";

                // Rule 1: SSU ID is required
                if (string.IsNullOrWhiteSpace(ssuId))
                {
                    validationStatus = StatusCodes.Invalid;
                    validationMessage = "ERR_SSU_REQUIRED: SSU ID is required.";
                    result.InvalidCount++;
                }
                // Rule 2: SSU ID must be a valid GUID
                else if (!Guid.TryParse(ssuId, out _))
                {
                    validationStatus = StatusCodes.Invalid;
                    validationMessage = "ERR_SSU_INVALID_GUID: SSU ID must be a valid GUID.";
                    result.InvalidCount++;
                }
                // Rule 3: Source value required only when configured
                else if (requireSourceValue && string.IsNullOrWhiteSpace(sourceValue))
                {
                    validationStatus = StatusCodes.Invalid;
                    validationMessage = "ERR_SOURCE_REQUIRED: Source value is required for this batch type.";
                    result.InvalidCount++;
                }
                // Rule 4: Check duplicate SSU IDs within this batch
                else if (!seenSsuIds.Add(ssuId))
                {
                    validationStatus = StatusCodes.Duplicate;
                    validationMessage = "ERR_DUP_SSU_SAME_BATCH: Duplicate SSU ID within this batch.";
                    isDuplicate = true;
                    //duplicateCategory = "Same Batch";
                    result.DuplicateCount++;
                }
                // Rule 5: Optional duplicate source value check (only when source value exists)
                else if (!string.IsNullOrWhiteSpace(sourceValue) && !seenSourceValues.Add(sourceValue))
                {
                    validationStatus = StatusCodes.Duplicate;
                    validationMessage = "ERR_DUP_SOURCE_SAME_BATCH: Duplicate source value within this batch.";
                    isDuplicate = true;
                    //duplicateCategory = "Same Batch";
                    result.DuplicateCount++;
                }
                // Rule 6: Optional cross-batch duplicate check
                else if (checkCrossBatchDuplicates)
                {
                    List<CrossBatchDuplicateBatchInfo> conflictingBatches;
                    try
                    {
                        conflictingBatches = await CrossBatchDuplicateLookupService.FindConflictingBatchesAsync(
                            _dataverseService,
                            _logger,
                            bulkProcessorId,
                            ssuId,
                            bulkIngestionItemEntityName,
                            bulkIngestionItemParentLookupColumnName,
                            ssuIdColumnName,
                            "voa_bulkingestion");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking if SSU ID {SsuId} exists in other batches", ssuId);
                        conflictingBatches = new List<CrossBatchDuplicateBatchInfo>();
                    }

                    if (conflictingBatches.Count > 0)
                    {
                        validationStatus = StatusCodes.Duplicate;
                        validationMessage = CrossBatchDuplicateMessageHelper.BuildErrorMessage(conflictingBatches);
                        isDuplicate = true;
                        //duplicateCategory = "Other Batch";
                        result.DuplicateCount++;
                        result.CrossBatchDuplicateBatches.AddRange(conflictingBatches);
                    }
                    else
                    {
                        result.ValidCount++;
                    }
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

                // if (isDuplicate && !string.IsNullOrWhiteSpace(duplicateCategory))
                // {
                //     updateEntity[duplicateCategoryColumnName] = duplicateCategory;
                // }

                updateRequests.Add(new Microsoft.Xrm.Sdk.Messages.UpdateRequest { Target = updateEntity });

                if (!string.IsNullOrWhiteSpace(validationMessage))
                {
                    validationErrors.Add($"ItemId={itemId}; Message={validationMessage}");
                }
            }

            // Execute batch updates
            if (updateRequests.Count > 0)
            {
                var writer = new DataverseBulkItemWriter(_dataverseService);
                var writeResult = writer.ExecuteItemRequests(updateRequests);

                _logger.LogInformation(
                    "Validation updates: {SuccessCount} succeeded, {FailureCount} failed",
                    writeResult.Result.SucceededOperationCount,
                    writeResult.Result.FailedOperationCount);

                result.UpdatedCount = writeResult.Result.SucceededOperationCount;
            }

            result.TotalCount = allItems.Entities.Count;
            result.InvalidCount = result.TotalCount - result.ValidCount - result.DuplicateCount;

            if (validationErrors.Count > 0)
            {
                _logger.LogWarning(
                    "Validation error details for batch {BulkProcessorId}. ErrorCount={ErrorCount}. Errors={Errors}",
                    bulkProcessorId,
                    validationErrors.Count,
                    string.Join(" || ", validationErrors));
            }

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

    private static string NormalizeValue(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static bool GetBooleanFlag(string key, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
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

    public List<CrossBatchDuplicateBatchInfo> CrossBatchDuplicateBatches { get; set; } = new();
}

