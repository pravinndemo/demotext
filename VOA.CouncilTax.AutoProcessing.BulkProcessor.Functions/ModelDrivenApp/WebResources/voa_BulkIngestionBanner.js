window.VOA = window.VOA || {};
var VOA = window.VOA;
VOA.BulkIngestionBanner = (function () {
    "use strict";

    var notificationId = "voa_bulk_ingestion_banner";
    var pollTimerId = null;
    var pollInFlight = false;
    var pollIntervalMs = 15000;

    var fields = {
        processingStatus: "voa_processingstatus",
        statusCode: "statuscode",
        submittedOn: "voa_submittedon",
        processingStartedOn: "voa_processingstartedon",
        processedOn: "voa_processedon",
        delayProcessingUntil: "voa_delayprocessinguntil",
        errorSummary: "voa_errorsummary"
    };

    var processingStatus = {
        Processed: 589160000,
        Processing: 589160001,
        Failed: 589160002
    };

    var statusCode = {
        Draft: 358800001,
        Queued: 358800002,
        PartialSuccess: 358800003,
        Delayed: 358800004,
        Completed: 358800009,
        Failed: 358800012
    };

    var levels = {
        Info: "INFO",
        Warning: "WARNING",
        Error: "ERROR"
    };

    function onLoad(executionContext) {
        var formContext = executionContext.getFormContext();
        registerHandlers(formContext);
        syncQueuedDelayEditability(formContext);
        updateBanner(formContext);
    }

    function onFieldChange(executionContext) {
        var formContext = executionContext.getFormContext();
        syncQueuedDelayEditability(formContext);
        updateBanner(formContext);
    }

    function registerHandlers(formContext) {
        addOnChange(formContext, fields.processingStatus);
        addOnChange(formContext, fields.statusCode);
        addOnChange(formContext, fields.submittedOn);
        addOnChange(formContext, fields.processingStartedOn);
        addOnChange(formContext, fields.processedOn);
        addOnChange(formContext, fields.delayProcessingUntil);
        addOnChange(formContext, fields.errorSummary);
    }

    function addOnChange(formContext, fieldName) {
        var attribute = formContext.getAttribute(fieldName);
        if (attribute) {
            attribute.addOnChange(onFieldChange);
        }
    }

    function updateBanner(formContext) {
        if (!formContext || !formContext.ui) {
            return;
        }

        var banner = buildBannerModel(formContext);
        clearBanner(formContext);

        if (!banner) {
            stopPolling();
            return;
        }

        formContext.ui.setFormNotification(banner.message, banner.level, notificationId);

        if (banner.autoClearMs) {
            window.setTimeout(function () {
                if (formContext && formContext.ui) {
                    formContext.ui.clearFormNotification(notificationId);
                }
            }, banner.autoClearMs);
        }

        if (banner.poll) {
            startPolling(formContext);
        } else {
            stopPolling();
        }
    }

    function syncQueuedDelayEditability(formContext) {
        if (!formContext) {
            return;
        }

        var statusValue = getChoiceValue(formContext, fields.statusCode);
        var delayAttribute = formContext.getAttribute(fields.delayProcessingUntil);
        if (!delayAttribute || !delayAttribute.controls) {
            return;
        }

        var shouldLockDelay = statusValue === statusCode.Processing
            || statusValue === statusCode.PartialSuccess
            || statusValue === statusCode.Completed
            || statusValue === statusCode.Failed;

        delayAttribute.controls.forEach(function (control) {
            if (control && typeof control.setDisabled === "function") {
                control.setDisabled(shouldLockDelay);
            }
        });
    }

    function buildBannerModel(formContext) {
        var processingValue = getChoiceValue(formContext, fields.processingStatus);
        var statusValue = getChoiceValue(formContext, fields.statusCode);
        var submittedOn = getDateValue(formContext, fields.submittedOn);
        var startedOn = getDateValue(formContext, fields.processingStartedOn);
        var processedOn = getDateValue(formContext, fields.processedOn);
        var errorSummary = getTextValue(formContext, fields.errorSummary);

        if (processingValue === processingStatus.Processing) {
            if (statusValue === statusCode.Draft) {
                return {
                    level: levels.Warning,
                    message: buildMessage(
                        "Batch job is running...",
                        "Saving items.",
                        startedOn ? "Started " + formatDateTime(startedOn) + "." : null,
                        "Please keep this record open until the banner clears."
                    ),
                    poll: true
                };
            }

            if (statusValue === statusCode.Queued) {
                return {
                    level: levels.Warning,
                    message: buildMessage(
                        "Batch job is running...",
                        "Submitting batch.",
                        startedOn ? "Started " + formatDateTime(startedOn) + "." : null,
                        "You can leave this form open or come back later."
                    ),
                    poll: true
                };
            }

            return {
                level: levels.Warning,
                message: buildMessage(
                    "Batch job is running...",
                    startedOn ? "Started " + formatDateTime(startedOn) + "." : null,
                    "You can continue working while the batch runs."
                ),
                poll: true
            };
        }

        if (processingValue === processingStatus.Failed || statusValue === statusCode.Failed) {
            return {
                level: levels.Error,
                message: buildMessage(
                    "Batch job failed.",
                    errorSummary ? "Review the error summary for details." : "Review the failure details on the record.",
                    "Fix the issue and try again."
                ),
                poll: false
            };
        }

        if (statusValue === statusCode.Queued) {
            return {
                level: levels.Info,
                message: buildMessage(
                    "Batch submitted.",
                    "Background processing will start shortly.",
                    submittedOn ? "Submitted at " + formatDateTime(submittedOn) + "." : null
                ),
                poll: true
            };
        }

        if (statusValue === statusCode.PartialSuccess) {
            return {
                level: levels.Warning,
                message: buildMessage(
                    "Batch job completed with some failed items.",
                    "Review the failed items before resubmitting.",
                    processedOn ? "Completed at " + formatDateTime(processedOn) + "." : null
                ),
                poll: false
            };
        }

        if (statusValue === statusCode.Delayed) {
            return {
                level: levels.Warning,
                message: buildMessage(
                    "Batch job is retrying automatically.",
                    "The system will try again shortly.",
                    "You do not need to resubmit yet."
                ),
                poll: true
            };
        }

        if ((statusValue === statusCode.Completed || processingValue === processingStatus.Processed) && isRecent(processedOn, 10)) {
            return {
                level: levels.Info,
                message: buildMessage(
                    "Batch job completed successfully.",
                    processedOn ? "Completed at " + formatDateTime(processedOn) + "." : null,
                    null
                ),
                autoClearMs: 4000,
                poll: false
            };
        }

        if (statusValue === statusCode.Draft) {
            if (processingValue === processingStatus.Processed && isRecent(processedOn, 2)) {
                return {
                    level: levels.Info,
                    message: buildMessage(
                        "Items saved successfully.",
                        processedOn ? "Completed at " + formatDateTime(processedOn) + "." : null,
                        null
                    ),
                    autoClearMs: 4000,
                    poll: false
                };
            }

            return {
                level: levels.Info,
                message: "Ready to save items.",
                poll: false
            };
        }

        return null;
    }

    function startPolling(formContext) {
        if (pollTimerId !== null) {
            return;
        }

        pollTimerId = window.setInterval(function () {
            if (pollInFlight) {
                return;
            }

            if (formContext.data && formContext.data.entity && formContext.data.entity.getIsDirty()) {
                return;
            }

            pollInFlight = true;
            formContext.data.refresh(false).then(function () {
                pollInFlight = false;
                updateBanner(formContext);
            }, function () {
                pollInFlight = false;
            });
        }, pollIntervalMs);
    }

    function stopPolling() {
        if (pollTimerId !== null) {
            window.clearInterval(pollTimerId);
            pollTimerId = null;
        }

        pollInFlight = false;
    }

    function clearBanner(formContext) {
        if (formContext && formContext.ui) {
            formContext.ui.clearFormNotification(notificationId);
        }
    }

    function getChoiceValue(formContext, fieldName) {
        var attribute = formContext.getAttribute(fieldName);
        return attribute ? attribute.getValue() : null;
    }

    function getDateValue(formContext, fieldName) {
        var attribute = formContext.getAttribute(fieldName);
        return attribute ? attribute.getValue() : null;
    }

    function getTextValue(formContext, fieldName) {
        var attribute = formContext.getAttribute(fieldName);
        var value = attribute ? attribute.getValue() : null;
        return value ? String(value).trim() : "";
    }

    function formatDateTime(value) {
        if (!value) {
            return "";
        }

        try {
            return value.toLocaleString();
        } catch (e) {
            return value.toString();
        }
    }

    function isRecent(value, minutes) {
        if (!value) {
            return false;
        }

        var now = new Date();
        var threshold = new Date(now.getTime() - (minutes * 60 * 1000));
        return value >= threshold;
    }

    function buildMessage(part1, part2, part3) {
        var parts = [];

        if (part1) {
            parts.push(part1);
        }

        if (part2) {
            parts.push(part2);
        }

        if (part3) {
            parts.push(part3);
        }

        return parts.join(" ");
    }

    return {
        onLoad: onLoad,
        onFieldChange: onFieldChange,
        updateBanner: updateBanner,
        clearBanner: clearBanner
    };
})();
