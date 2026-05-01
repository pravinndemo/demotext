using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Routing;

public static class BulkDataRouteDecisionBuilder
{
    public static BulkDataRouteDecisionResponse BuildDecision(BulkDataRouteDecisionRequest request)
    {
        var hasBulkProcessorId = request.BulkProcessorId != Guid.Empty;
        var hasSsuIds = request.SsuIds is { Count: > 0 };
        var hasSvtProcessingId = request.SvtProcessingId.HasValue && request.SvtProcessingId.Value != Guid.Empty;
        var hasLegacySvtField = !string.IsNullOrWhiteSpace(request.SsuId)
            || !string.IsNullOrWhiteSpace(request.UserId)
            || !string.IsNullOrWhiteSpace(request.ComponentName);
        var hasAnySvtField = hasLegacySvtField || hasSvtProcessingId;

        if (hasBulkProcessorId && hasAnySvtField)
        {
            return new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "INVALID_COMBINATION",
                Message = "Do not mix bulk fields (bulkProcessorId/ssuIds) with SVT fields (ssuid/userId/componentName/svtProcessingId).",
            };
        }

        if (hasSvtProcessingId)
        {
            if (hasLegacySvtField)
            {
                return new BulkDataRouteDecisionResponse
                {
                    Accepted = false,
                    Code = "INVALID_COMBINATION",
                    Message = "SVT tracking dispatch must supply svtProcessingId only.",
                };
            }

            return new BulkDataRouteDecisionResponse
            {
                Accepted = true,
                RouteMode = "SVT_TRACKING",
                SvtProcessingId = request.SvtProcessingId,
            };
        }

        if (hasBulkProcessorId && hasSsuIds)
        {
            return new BulkDataRouteDecisionResponse
            {
                Accepted = true,
                RouteMode = "BULK_SELECTION",
                SsuIds = request.SsuIds!.Select(item => item.StatutorySpatialUnitId.ToString()).ToList(),
            };
        }

        if (hasBulkProcessorId)
        {
            return new BulkDataRouteDecisionResponse
            {
                Accepted = true,
                RouteMode = "BULK_FILE",
            };
        }

        if (!hasBulkProcessorId && hasAnySvtField)
        {
            return new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "INVALID_SVT_REQUEST",
                Message = "SVT tracking mode requires svtProcessingId. Legacy direct SVT fields are no longer supported.",
            };
        }

        if (request.SsuIds is { Count: > 0 })
        {
            return new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "BULK_PROCESSOR_ID_REQUIRED",
                Message = "bulkProcessorId is required when ssuIds are provided for bulk selection mode.",
            };
        }

        return new BulkDataRouteDecisionResponse
        {
            Accepted = false,
            Code = "INVALID_COMBINATION",
            Message = "Valid combinations are: bulkProcessorId + ssuIds[], bulkProcessorId only, or svtProcessingId.",
        };
    }
}

