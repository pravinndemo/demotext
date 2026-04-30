using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using System.Text.Json;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Constants;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;
using VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Services;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Activities;

public sealed partial class BulkDataRequestProcessor
{
    private async Task<IActionResult> HandleSvtTrackingAsync(BulkDataRouteDecisionRequest request)
    {
        var svtProcessingId = request.SvtProcessingId ?? Guid.Empty;
        if (svtProcessingId == Guid.Empty)
        {
            return new BadRequestObjectResult(new BulkDataRouteDecisionResponse
            {
                Accepted = false,
                Code = "SVT_PROCESSING_ID_REQUIRED",
                Message = "svtProcessingId is required.",
                CorrelationId = request.CorrelationId,
                RouteMode = "SVT_TRACKING",
            });
        }

        _logger.LogInformation(
            "Processing SVT tracking request. SvtProcessingId={SvtProcessingId}, CorrelationId={CorrelationId}",
            svtProcessingId,
            request.CorrelationId);

        var trackingRecord = await _svtTrackingService.RetrieveAsync(svtProcessingId, request.CorrelationId);
        if (trackingRecord is null)
        {
            return new ObjectResult(BuildSvtTrackingResponse(
                svtProcessingId,
                request.CorrelationId,
                accepted: false,
                code: "SVT_TRACKING_LOOKUP_FAILED",
                message: "Unable to read SVT processing row from Dataverse."))
            {
                StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError,
            };
        }

        var correlationId = trackingRecord.CorrelationId ?? request.CorrelationId;

        if (trackingRecord.StatusCode == SvtProcessingConstants.StatusCodes.Completed)
        {
            return new AcceptedResult(string.Empty, BuildSvtTrackingResponse(
                trackingRecord.Id,
                correlationId,
                accepted: true,
                code: "SVT_ALREADY_PROCESSED",
                message: "SVT processing row is already completed.",
                trackingRecord));
        }

        if (trackingRecord.StatusCode == SvtProcessingConstants.StatusCodes.Processing)
        {
            return new AcceptedResult(string.Empty, BuildSvtTrackingResponse(
                trackingRecord.Id,
                correlationId,
                accepted: true,
                code: "SVT_ALREADY_PROCESSING",
                message: "SVT processing row is already being processed.",
                trackingRecord));
        }

        if (trackingRecord.DispatchStateCode is not (SvtProcessingConstants.DispatchStateCodes.Requested or SvtProcessingConstants.DispatchStateCodes.ReRequested))
        {
            return new BadRequestObjectResult(BuildSvtTrackingResponse(
                trackingRecord.Id,
                correlationId,
                accepted: false,
                code: "SVT_DISPATCH_NOT_REQUESTED",
                message: "SVT tracking row is not in a requested dispatch state.",
                trackingRecord));
        }

        if (string.IsNullOrWhiteSpace(trackingRecord.SsuId) || string.IsNullOrWhiteSpace(trackingRecord.UserId) || string.IsNullOrWhiteSpace(trackingRecord.ComponentName))
        {
            await _svtTrackingService.MarkFailedAsync(
                trackingRecord.Id,
                "INVALID_SVT_REQUEST",
                "SVT tracking row is missing required fields.",
                isRetryable: false,
                correlationId);

            return new BadRequestObjectResult(BuildSvtTrackingResponse(
                trackingRecord.Id,
                correlationId,
                accepted: false,
                code: "INVALID_SVT_REQUEST",
                message: "SVT tracking row must contain ssuid, userId, and componentName.",
                trackingRecord));
        }

        if (trackingRecord.RequestId.HasValue && trackingRecord.JobId.HasValue)
        {
            return new AcceptedResult(string.Empty, BuildSvtTrackingResponse(
                trackingRecord.Id,
                correlationId,
                accepted: true,
                code: "SVT_ALREADY_PROCESSED",
                message: "SVT request/job already exists.",
                trackingRecord));
        }

        try
        {
            await _svtTrackingService.MarkProcessingAsync(trackingRecord.Id, correlationId);

            var requestJobService = new RequestJobCreationService(_dataverseService, _logger);
            var requestItem = new RequestJobCreateItem
            {
                SsuId = trackingRecord.SsuId!,
                SourceType = "SVT",
                SourceValue = trackingRecord.SsuId!,
            };

            RequestJobCreateResult requestResult;
            if (!trackingRecord.RequestId.HasValue)
            {
                requestResult = await requestJobService.CreateRequestOnlyAsync(
                    requestItem,
                    trackingRecord.UserId!,
                    trackingRecord.ComponentName!,
                    sourceType: "SVT");

                if (!requestResult.Success)
                {
                    if (string.Equals(requestResult.ErrorCode, "ACTIVE_REQUEST_PRESENT", StringComparison.OrdinalIgnoreCase))
                    {
                        var existingRequestId = await requestJobService.TryGetActiveRequestIdAsync(
                            Guid.Parse(trackingRecord.SsuId!),
                            trackingRecord.ComponentName!);

                        if (existingRequestId.HasValue)
                        {
                            requestResult = new RequestJobCreateResult
                            {
                                Success = true,
                                RequestId = existingRequestId.Value,
                                SourceType = "SVT",
                            };
                        }
                    }

                    if (!requestResult.Success)
                    {
                        await _svtTrackingService.MarkFailedAsync(
                            trackingRecord.Id,
                            requestResult.ErrorCode ?? "SVT_REQUEST_FAILED",
                            requestResult.ErrorMessage ?? "SVT request creation failed.",
                            isRetryable: true,
                            correlationId);

                        return new ObjectResult(BuildSvtTrackingResponse(
                            trackingRecord.Id,
                            correlationId,
                            accepted: false,
                            code: requestResult.ErrorCode ?? "SVT_REQUEST_FAILED",
                            message: requestResult.ErrorMessage ?? "SVT request creation failed.",
                            trackingRecord))
                        {
                            StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError,
                        };
                    }
                }

                await _svtTrackingService.MarkRequestCreatedAsync(
                    trackingRecord.Id,
                    requestResult.RequestId,
                    correlationId);
            }
            else
            {
                requestResult = new RequestJobCreateResult
                {
                    Success = true,
                    RequestId = trackingRecord.RequestId.Value,
                    SourceType = "SVT",
                };
            }

            var jobId = await requestJobService.TryGetExistingJobIdForRequestAsync(requestResult.RequestId);
            if (!jobId.HasValue)
            {
                jobId = await requestJobService.CreateJobForRequestAsync(
                    requestResult.RequestId,
                    requestItem,
                    trackingRecord.UserId!,
                    trackingRecord.ComponentName!);
            }

            await _svtTrackingService.MarkCompletedAsync(
                trackingRecord.Id,
                requestResult.RequestId,
                jobId.Value,
                correlationId);

            return new AcceptedResult(string.Empty, BuildSvtTrackingResponse(
                trackingRecord.Id,
                correlationId,
                accepted: true,
                code: "SVT_COMPLETED",
                message: $"SVT request and job created successfully. RequestId={requestResult.RequestId:N}, JobId={jobId.Value:N}",
                trackingRecord,
                requestResult.RequestId,
                jobId.Value,
                status: "Completed",
                attemptCount: trackingRecord.AttemptCount + 1));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing SVT tracking request. SvtProcessingId={SvtProcessingId}, CorrelationId={CorrelationId}",
                trackingRecord.Id,
                correlationId);

            await _svtTrackingService.MarkFailedAsync(
                trackingRecord.Id,
                "SVT_JOB_FAILED",
                ex.Message,
                isRetryable: true,
                correlationId);

            return new ObjectResult(BuildSvtTrackingResponse(
                trackingRecord.Id,
                correlationId,
                accepted: false,
                code: "SVT_JOB_FAILED",
                message: "SVT request/job processing failed. See logs for details.",
                trackingRecord,
                status: "Failed",
                attemptCount: trackingRecord.AttemptCount + 1))
            {
                StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError,
            };
        }
    }

    private static BulkDataRouteDecisionResponse BuildSvtTrackingResponse(
        Guid svtProcessingId,
        string? correlationId,
        bool accepted,
        string code,
        string message,
        SvtProcessingRecord? trackingRecord = null,
        Guid? requestId = null,
        Guid? jobId = null,
        string? status = null,
        int? attemptCount = null)
    {
        return new BulkDataRouteDecisionResponse
        {
            Accepted = accepted,
            Code = code,
            Message = message,
            SvtProcessingId = svtProcessingId,
            CorrelationId = correlationId,
            Action = "SvtTracking",
            RouteMode = "SVT_TRACKING",
            DispatchState = trackingRecord?.DispatchState,
            Status = status ?? trackingRecord?.Status,
            RequestId = requestId ?? trackingRecord?.RequestId,
            JobId = jobId ?? trackingRecord?.JobId,
            AttemptCount = attemptCount ?? trackingRecord?.AttemptCount,
            SsuId = trackingRecord?.SsuId,
            UserId = trackingRecord?.UserId,
            ComponentName = trackingRecord?.ComponentName,
        };
    }
}
