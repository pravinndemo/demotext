using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Models;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Routing;

public static class BulkDataRouteDecisionBuilder
{
    public static BulkDataRouteDecisionResponse BuildDecision(BulkDataRouteDecisionRequest request)
    {
        var hasBulkProcessorId = request.BulkProcessorId != Guid.Empty;
        var hasSsuIds = request.SsuIds is { Count: > 0 };
        var hasSingleSsuId = !string.IsNullOrWhiteSpace(request.SsuId);
        var hasUserId = !string.IsNullOrWhiteSpace(request.UserId);
        var hasComponentName = !string.IsNullOrWhiteSpace(request.ComponentName);

        var hasAnySvtField = hasSingleSsuId || hasUserId || hasComponentName;

        if (hasBulkProcessorId && hasAnySvtField)
        {
            return new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "INVALID_COMBINATION",
                Message = "Do not mix bulk fields (bulkProcessorId/ssuIds) with SVT fields (ssuid/userId/componentName).",
            };
        }

        if (hasBulkProcessorId && hasSsuIds)
        {
            return new BulkDataRouteDecisionResponse
            {
                Accepted = true,
                RouteMode = "BULK_SELECTION",
                SsuIds = request.SsuIds,
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
            if (!hasSingleSsuId || !hasUserId || !hasComponentName)
            {
                return new BulkDataRouteDecisionResponse
                {
                    Accepted = false,
                    Code = "INVALID_SVT_REQUEST",
                    Message = "SVT mode requires ssuid, userId, and componentName.",
                };
            }

            return new BulkDataRouteDecisionResponse
            {
                Accepted = true,
                RouteMode = "SVT_SINGLE",
                SsuId = request.SsuId,
                UserId = request.UserId,
                ComponentName = request.ComponentName,
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
            Message = "Valid combinations are: bulkProcessorId + ssuIds[], bulkProcessorId only, or ssuid + userId + componentName.",
        };
    }
}
