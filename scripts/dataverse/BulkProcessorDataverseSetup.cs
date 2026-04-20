using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace BulkProcessor.DataverseSetup;

internal static class Program
{
    private const int LcidEnUs = 1033;

    private const string BulkIngestionSchema = "voa_BulkIngestion";
    private const string BulkIngestionLogical = "voa_bulkingestion";

    private const string BulkIngestionItemSchema = "voa_BulkIngestionItem";
    private const string BulkIngestionItemLogical = "voa_bulkingestionitem";

    private const string BulkIngestionTemplateSchema = "voa_BulkIngestionTemplate";
    private const string BulkIngestionTemplateLogical = "voa_bulkingestiontemplate";

    // Global choice "Bulk ingestion source" - shared across tables (CSV, External System, System Entered)
    private static readonly string[] SourceChoiceValues = { "CSV", "External System", "System Entered" };

    private static int Main(string[] args)
    {
        try
        {
            var options = ParseArgs(args);
            if (string.IsNullOrWhiteSpace(options.EnvironmentUrl))
            {
                Console.Error.WriteLine("Missing required argument: --url https://<org>.crm.dynamics.com");
                return 2;
            }

            var connectionString = BuildConnectionString(options);
            using var serviceClient = new ServiceClient(connectionString);
            if (!serviceClient.IsReady)
            {
                Console.Error.WriteLine("Failed to connect to Dataverse.");
                Console.Error.WriteLine(serviceClient.LastError);
                return 1;
            }

            Console.WriteLine("Connected: " + serviceClient.ConnectedOrgUriActual);

            if (!string.IsNullOrWhiteSpace(options.SolutionName))
            {
                Console.WriteLine("Ensuring solution: " + options.SolutionName);
                EnsureSolution(serviceClient, options);
            }

            EnsureBulkIngestionTemplateEntity(serviceClient, options);
            EnsureBulkIngestionEntity(serviceClient, options);
            EnsureBulkIngestionItemEntity(serviceClient, options);
            EnsureCoreRelationships(serviceClient, options);

            if (options.Publish)
            {
                Console.WriteLine("Publishing customizations...");
                serviceClient.Execute(new PublishAllXmlRequest());
            }

            Console.WriteLine("Dataverse setup completed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static SetupOptions ParseArgs(string[] args)
    {
        var options = new SetupOptions
        {
            SolutionName = "CTP_BulkData_Creation",
            PublisherName = "voa",
            PublisherPrefix = "voa"
        };
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var next = i + 1 < args.Length ? args[i + 1] : null;

            switch (arg)
            {
                case "--url":
                    options.EnvironmentUrl = next;
                    i++;
                    break;
                case "--clientId":
                    options.ClientId = next;
                    i++;
                    break;
                case "--redirectUri":
                    options.RedirectUri = next;
                    i++;
                    break;
                case "--requestEntity":
                    options.RequestEntityLogicalName = next;
                    i++;
                    break;
                case "--jobEntity":
                    options.JobEntityLogicalName = next;
                    i++;
                    break;
                case "--solution":
                    options.SolutionName = next;
                    i++;
                    break;
                case "--publisher":
                    options.PublisherName = next;
                    i++;
                    break;
                case "--prefix":
                    options.PublisherPrefix = next;
                    i++;
                    break;
                case "--noPublish":
                    options.Publish = false;
                    break;
                case "--columnsOnly":
                    options.ColumnsOnly = true;
                    break;
                default:
                    break;
            }
        }

        return options;
    }

    private static string BuildConnectionString(SetupOptions options)
    {
        var clientId = string.IsNullOrWhiteSpace(options.ClientId)
            ? "51f81489-12ee-4a9e-aaae-a2591f45987d"
            : options.ClientId;

        var redirectUri = string.IsNullOrWhiteSpace(options.RedirectUri)
            ? "http://localhost"
            : options.RedirectUri;

        // LoginPrompt=Auto uses the signed-in user context when available and prompts only when needed.
        return $"AuthType=OAuth;Url={options.EnvironmentUrl};ClientId={clientId};RedirectUri={redirectUri};LoginPrompt=Auto;RequireNewInstance=true";
    }

    private static void EnsureSolution(IOrganizationService service, SetupOptions options)
    {
        try
        {
            var request = new RetrieveEntityRequest
            {
                EntityFilters = EntityFilters.Entity,
                LogicalName = "solution"
            };
            service.Execute(request);
        }
        catch
        {
            // Solution entity exists, continue
        }

        var query = new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("solutionid", "uniquename"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("uniquename", ConditionOperator.Equal, options.SolutionName)
                }
            }
        };

        var result = service.RetrieveMultiple(query);
        if (result.Entities.Count > 0)
        {
            Console.WriteLine($"Solution {options.SolutionName} already exists.");
            return;
        }

        Console.WriteLine($"Creating solution: {options.SolutionName}");
        var solutionEntity = new Entity("solution")
        {
            ["uniquename"] = options.SolutionName,
            ["friendlyname"] = options.SolutionName,
            ["publisherid"] = new EntityReference("publisher", GetOrCreatePublisher(service, options))
        };

        service.Create(solutionEntity);
    }

    private static Guid GetOrCreatePublisher(IOrganizationService service, SetupOptions options)
    {
        var query = new QueryExpression("publisher")
        {
            ColumnSet = new ColumnSet("publisherid", "uniquename"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("uniquename", ConditionOperator.Equal, options.PublisherName?.ToLowerInvariant() ?? "default")
                }
            }
        };

        var result = service.RetrieveMultiple(query);
        if (result.Entities.Count > 0)
        {
            return result.Entities[0].Id;
        }

        Console.WriteLine($"Creating publisher: {options.PublisherName}");
        var publisherEntity = new Entity("publisher")
        {
            ["uniquename"] = options.PublisherName?.ToLowerInvariant() ?? "voa",
            ["friendlyname"] = options.PublisherName ?? "VOA",
            ["customizationprefix"] = options.PublisherPrefix ?? "voa"
        };

        return service.Create(publisherEntity);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Bulk Ingestion Template (2.3)
    // ──────────────────────────────────────────────────────────────────────────
    private static void EnsureBulkIngestionTemplateEntity(IOrganizationService service, SetupOptions options)
    {
        if (!EntityExists(service, BulkIngestionTemplateLogical))
        {
            if (options.ColumnsOnly)
            {
                Console.WriteLine($"Skipping table {BulkIngestionTemplateLogical}: --columnsOnly enabled and table does not exist.");
                return;
            }

            Console.WriteLine("Creating table: " + BulkIngestionTemplateLogical);
            var createEntityRequest = new CreateEntityRequest
            {
                Entity = new EntityMetadata
                {
                    SchemaName = BulkIngestionTemplateSchema,
                    DisplayName = Localized("Bulk Ingestion Template"),
                    DisplayCollectionName = Localized("Bulk Ingestion Templates"),
                    Description = Localized("Predefined template / schema helper for bulk ingestion formats."),
                    OwnershipType = OwnershipTypes.OrganizationOwned,
                    IsActivity = false,
                    HasNotes = false,
                    HasActivities = false
                },
                PrimaryAttribute = new StringAttributeMetadata
                {
                    SchemaName = "voa_name",
                    DisplayName = Localized("Template Name"),
                    RequiredLevel = Required(AttributeRequiredLevel.ApplicationRequired),
                    MaxLength = 200,
                    FormatName = StringFormatName.Text,
                    Description = Localized("Template display name.")
                }
            };

            service.Execute(createEntityRequest);
            EnsureEntityInSolution(service, BulkIngestionTemplateLogical, options);
        }
        else
        {
            EnsureEntityInSolution(service, BulkIngestionTemplateLogical, options);
        }

        // voa_format: sync with global choice "Bulk ingestion source" (CSV, External System, System Entered)
        EnsureChoiceColumn(service, BulkIngestionTemplateLogical, "voa_Format", "Format", false, SourceChoiceValues, options);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Bulk Ingestion (2.1) - header / master table
    // ──────────────────────────────────────────────────────────────────────────
    private static void EnsureBulkIngestionEntity(IOrganizationService service, SetupOptions options)
    {
        if (!EntityExists(service, BulkIngestionLogical))
        {
            if (options.ColumnsOnly)
            {
                Console.WriteLine($"Skipping table {BulkIngestionLogical}: --columnsOnly enabled and table does not exist.");
                return;
            }

            Console.WriteLine("Creating table: " + BulkIngestionLogical);
            var createEntityRequest = new CreateEntityRequest
            {
                Entity = new EntityMetadata
                {
                    SchemaName = BulkIngestionSchema,
                    DisplayName = Localized("Bulk Ingestion"),
                    DisplayCollectionName = Localized("Bulk Ingestions"),
                    Description = Localized("Tracks bulk ingestion batches."),
                    OwnershipType = OwnershipTypes.UserOwned,
                    IsActivity = false,
                    HasNotes = true,
                    HasActivities = false
                },
                PrimaryAttribute = new StringAttributeMetadata
                {
                    SchemaName = "voa_BatchName",
                    DisplayName = Localized("Batch Name"),
                    RequiredLevel = Required(AttributeRequiredLevel.ApplicationRequired),
                    MaxLength = 200,
                    FormatName = StringFormatName.Text,
                    Description = Localized("User-friendly batch name.")
                }
            };

            service.Execute(createEntityRequest);
            EnsureEntityInSolution(service, BulkIngestionLogical, options);
        }
        else
        {
            EnsureEntityInSolution(service, BulkIngestionLogical, options);
        }

        EnsureStringColumn(service, BulkIngestionLogical, "voa_BatchReference", "Batch Reference", 100, true, options);

        // voa_source: sync with global choice "Bulk ingestion source"
        EnsureChoiceColumn(service, BulkIngestionLogical, "voa_Source", "Source", true, SourceChoiceValues, options);

        // voa_processingjobtype: Lookup to voa_codedreason (not a local choice)
        EnsureLookupRelationship(service, new LookupRelationshipSpec(
            ReferencedEntity: "voa_codedreason",
            ReferencingEntity: BulkIngestionLogical,
            LookupSchemaName: "voa_ProcessingJobType",
            LookupDisplayName: "Processing Job Type",
            RelationshipSchemaName: "voa_bulkingestion_processingjobtype_codedreason"), options);

        // Use OOTB statecode/statuscode for Bulk Ingestion lifecycle.
        EnsureBulkIngestionStateAndStatus(service);

        EnsureChoiceColumn(service, BulkIngestionLogical, "voa_AssignmentMode", "Assignment Mode", true, new[]
        {
            "Team", "Manager"
        }, options);

        EnsureLookupRelationship(service, new LookupRelationshipSpec(
            ReferencedEntity: "team",
            ReferencingEntity: BulkIngestionLogical,
            LookupSchemaName: "voa_AssignedTeam",
            LookupDisplayName: "Assigned Team",
            RelationshipSchemaName: "voa_bulkingestion_assignedteam_team"), options);

        EnsureLookupRelationship(service, new LookupRelationshipSpec(
            ReferencedEntity: "systemuser",
            ReferencingEntity: BulkIngestionLogical,
            LookupSchemaName: "voa_AssignedManager",
            LookupDisplayName: "Assigned Manager",
            RelationshipSchemaName: "voa_bulkingestion_assignedmanager_systemuser"), options);

        EnsureStringColumn(service, BulkIngestionLogical, "voa_FileReference", "File Reference", 500, false, options);
        EnsureStringColumn(service, BulkIngestionLogical, "voa_FileOriginalName", "File Original Name", 255, false, options);

        EnsureWholeNumberColumn(service, BulkIngestionLogical, "voa_TotalRows", "Total Rows", true, 0, 100000000, options);
        EnsureWholeNumberColumn(service, BulkIngestionLogical, "voa_ValidItemCount", "Valid Item Count", true, 0, 100000000, options);
        EnsureWholeNumberColumn(service, BulkIngestionLogical, "voa_InvalidItemCount", "Invalid Item Count", true, 0, 100000000, options);
        EnsureWholeNumberColumn(service, BulkIngestionLogical, "voa_DuplicateItemCount", "Duplicate Item Count", true, 0, 100000000, options);
        EnsureWholeNumberColumn(service, BulkIngestionLogical, "voa_ProcessedItemCount", "Processed Item Count", true, 0, 100000000, options);
        EnsureWholeNumberColumn(service, BulkIngestionLogical, "voa_FailedItemCount", "Failed Item Count", true, 0, 100000000, options);

        EnsureDateTimeColumn(service, BulkIngestionLogical, "voa_SubmittedOn", "Submitted On", false, options);
        EnsureDateTimeColumn(service, BulkIngestionLogical, "voa_ProcessingStartedOn", "Processing Started On", false, options);
        EnsureDateTimeColumn(service, BulkIngestionLogical, "voa_ProcessedOn", "Processed On", false, options);

        EnsureStringColumn(service, BulkIngestionLogical, "voa_ProcessingRunId", "Processing Run Id", 100, false, options);
        EnsureMultilineColumn(service, BulkIngestionLogical, "voa_ErrorSummary", "Error Summary", 2000, false, options);

        EnsureLookupRelationship(service, new LookupRelationshipSpec(
            ReferencedEntity: "systemuser",
            ReferencingEntity: BulkIngestionLogical,
            LookupSchemaName: "voa_LastActionBy",
            LookupDisplayName: "Last Action By",
            RelationshipSchemaName: "voa_bulkingestion_lastactionby_systemuser"), options);

        EnsureDateTimeColumn(service, BulkIngestionLogical, "voa_LastActionOn", "Last Action On", false, options);

        EnsureBooleanColumn(service, BulkIngestionLogical, "voa_RetriggerFailed", "Re-trigger Failed", false, "No", "Yes", options);
        EnsureDateTimeColumn(service, BulkIngestionLogical, "voa_DelayProcessingUntil", "Delay Processing Until", false, options);
        EnsureDateTimeColumn(service, BulkIngestionLogical, "voa_ActualStartTime", "Actual Start Time", false, options);
        EnsureDateTimeColumn(service, BulkIngestionLogical, "voa_ActualCompletionTime", "Actual Completion Time", false, options);

        // voa_Template: Lookup to Bulk Ingestion Template
        EnsureLookupRelationship(service, new LookupRelationshipSpec(
            ReferencedEntity: BulkIngestionTemplateLogical,
            ReferencingEntity: BulkIngestionLogical,
            LookupSchemaName: "voa_Template",
            LookupDisplayName: "Template",
            RelationshipSchemaName: "voa_bulkingestion_template_bulkingestiontemplate"), options);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Bulk Ingestion Item (2.2) - child / detail table
    // ──────────────────────────────────────────────────────────────────────────
    private static void EnsureBulkIngestionItemEntity(IOrganizationService service, SetupOptions options)
    {
        if (!EntityExists(service, BulkIngestionItemLogical))
        {
            if (options.ColumnsOnly)
            {
                Console.WriteLine($"Skipping table {BulkIngestionItemLogical}: --columnsOnly enabled and table does not exist.");
                return;
            }

            Console.WriteLine("Creating table: " + BulkIngestionItemLogical);
            var createEntityRequest = new CreateEntityRequest
            {
                Entity = new EntityMetadata
                {
                    SchemaName = BulkIngestionItemSchema,
                    DisplayName = Localized("Bulk Ingestion Item"),
                    DisplayCollectionName = Localized("Bulk Ingestion Items"),
                    Description = Localized("Tracks row-level staging and processing outcomes."),
                    OwnershipType = OwnershipTypes.UserOwned,
                    IsActivity = false,
                    HasNotes = true,
                    HasActivities = false
                },
                PrimaryAttribute = new StringAttributeMetadata
                {
                    SchemaName = "voa_Name",
                    DisplayName = Localized("Item Name"),
                    RequiredLevel = Required(AttributeRequiredLevel.ApplicationRequired),
                    MaxLength = 200,
                    FormatName = StringFormatName.Text,
                    Description = Localized("Friendly identifier for the item.")
                }
            };

            service.Execute(createEntityRequest);
            EnsureEntityInSolution(service, BulkIngestionItemLogical, options);
        }
        else
        {
            EnsureEntityInSolution(service, BulkIngestionItemLogical, options);
        }

        // voa_source: sync with global choice "Bulk ingestion source" (copied from parent)
        EnsureChoiceColumn(service, BulkIngestionItemLogical, "voa_Source", "Source", true, SourceChoiceValues, options);

        EnsureStringColumn(service, BulkIngestionItemLogical, "voa_SSUId", "SSU Id", 100, false, options);
        EnsureStringColumn(service, BulkIngestionItemLogical, "voa_HereditamentReference", "Hereditament Reference", 100, false, options);
        EnsureStringColumn(service, BulkIngestionItemLogical, "voa_SourceValue", "Source Value", 255, true, options);
        EnsureWholeNumberColumn(service, BulkIngestionItemLogical, "voa_SourceRowNumber", "Source Row Number", false, 0, 100000000, options);

        EnsureChoiceColumn(service, BulkIngestionItemLogical, "voa_ValidationStatus", "Validation Status", true, new[]
        {
            "Pending", "Valid", "Invalid", "Duplicate", "Processed", "Failed"
        }, options);
        EnsureMultilineColumn(service, BulkIngestionItemLogical, "voa_ValidationMessage", "Validation Message", 4000, false, options);

        EnsureBooleanColumn(service, BulkIngestionItemLogical, "voa_IsDuplicate", "Is Duplicate", true, "No", "Yes", options);
        EnsureChoiceColumn(service, BulkIngestionItemLogical, "voa_DuplicateCategory", "Duplicate Category", false, new[]
        {
            "Same Batch", "Existing Open Batch", "Existing Active Work"
        }, options);

        EnsureLookupRelationship(service, new LookupRelationshipSpec(
            ReferencedEntity: "team",
            ReferencingEntity: BulkIngestionItemLogical,
            LookupSchemaName: "voa_AssignedTeam",
            LookupDisplayName: "Assigned Team",
            RelationshipSchemaName: "voa_bulkingestionitem_assignedteam_team"), options);

        EnsureLookupRelationship(service, new LookupRelationshipSpec(
            ReferencedEntity: "systemuser",
            ReferencingEntity: BulkIngestionItemLogical,
            LookupSchemaName: "voa_AssignedManager",
            LookupDisplayName: "Assigned Manager",
            RelationshipSchemaName: "voa_bulkingestionitem_assignedmanager_systemuser"), options);

        EnsureStringColumn(service, BulkIngestionItemLogical, "voa_RequestIdText", "Request Line Item Id (Text)", 100, false, options);
        EnsureStringColumn(service, BulkIngestionItemLogical, "voa_JobIdText", "Incident Id (Text)", 100, false, options);

        EnsureWholeNumberColumn(service, BulkIngestionItemLogical, "voa_ProcessingAttemptCount", "Processing Attempt Count", true, 0, 1000000, options);
        EnsureDateTimeColumn(service, BulkIngestionItemLogical, "voa_ProcessingTimestamp", "Processing Timestamp", false, options);
        EnsureStringColumn(service, BulkIngestionItemLogical, "voa_ProcessingRunId", "Processing Run Id", 100, false, options);
        EnsureBooleanColumn(service, BulkIngestionItemLogical, "voa_CanReprocess", "Can Reprocess", true, "No", "Yes", options);
        EnsureBooleanColumn(service, BulkIngestionItemLogical, "voa_LockedForProcessing", "Locked For Processing", true, "No", "Yes", options);
        EnsureMultilineColumn(service, BulkIngestionItemLogical, "voa_RawPayload", "Raw Payload", 4000, false, options);

        EnsureChoiceColumn(service, BulkIngestionItemLogical, "voa_ProcessingStage", "Processing Stage", false, new[]
        {
            "Staging", "Validation", "voa_requestlineitem Creation", "Job Creation", "Completed"
        }, options);
    }

    private static void EnsureCoreRelationships(IOrganizationService service, SetupOptions options)
    {
        if (!EntityExists(service, BulkIngestionLogical) || !EntityExists(service, BulkIngestionItemLogical))
        {
            Console.WriteLine("Skipping core relationships: Bulk Ingestion tables not available in this environment.");
            return;
        }

        // Parent Bulk Ingestion → child Bulk Ingestion Item
        EnsureLookupRelationship(service, new LookupRelationshipSpec(
            ReferencedEntity: BulkIngestionLogical,
            ReferencingEntity: BulkIngestionItemLogical,
            LookupSchemaName: "voa_ParentBulkIngestion",
            LookupDisplayName: "Parent Bulk Ingestion",
            RelationshipSchemaName: "voa_bulkingestion_parentbulkingestion_bulkingestionitem"), options);

        // voa_requestlookup → voa_requestlineitem
        if (!string.IsNullOrWhiteSpace(options.RequestEntityLogicalName))
        {
            EnsureLookupRelationship(service, new LookupRelationshipSpec(
                ReferencedEntity: options.RequestEntityLogicalName,
                ReferencingEntity: BulkIngestionItemLogical,
                LookupSchemaName: "voa_RequestLookup",
            LookupDisplayName: "Request Line Item (voa_requestlineitem)",
            RelationshipSchemaName: "voa_bulkingestionitem_requestlookup_voa_requestlineitem"), options);
        }

        // voa_joblookup → incident
        if (!string.IsNullOrWhiteSpace(options.JobEntityLogicalName))
        {
            EnsureLookupRelationship(service, new LookupRelationshipSpec(
                ReferencedEntity: options.JobEntityLogicalName,
                ReferencingEntity: BulkIngestionItemLogical,
                LookupSchemaName: "voa_JobLookup",
                LookupDisplayName: "Job",
                RelationshipSchemaName: "voa_bulkingestionitem_joblookup_incident"), options);
        }
    }

    private static void AddComponentToSolution(IOrganizationService service, string solutionName, string componentType, Guid componentId)
    {
        var request = new AddSolutionComponentRequest
        {
            ComponentType = componentType switch
            {
                "Entity" => 1,  // Entity
                "Attribute" => 2,  // Attribute
                "Relationship" => 3,  // Relationship
                _ => 0
            },
            ComponentId = componentId,
            SolutionUniqueName = solutionName,
            AddRequiredComponents = false
        };

        try
        {
            service.Execute(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not add {componentType} {componentId} to solution: {ex.Message}");
        }
    }

    private static void EnsureEntityInSolution(IOrganizationService service, string entityLogicalName, SetupOptions? options)
    {
        if (string.IsNullOrWhiteSpace(options?.SolutionName))
        {
            return;
        }

        var request = new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = EntityFilters.Entity,
            RetrieveAsIfPublished = false
        };

        var response = (RetrieveEntityResponse)service.Execute(request);
        if (response?.EntityMetadata?.MetadataId.HasValue == true)
        {
            AddComponentToSolution(service, options.SolutionName, "Entity", response.EntityMetadata.MetadataId.Value);
        }
    }

    private static void EnsureAttributeInSolution(IOrganizationService service, string entityLogicalName, string attributeSchemaName, SetupOptions? options)
    {
        if (string.IsNullOrWhiteSpace(options?.SolutionName))
        {
            return;
        }

        var request = new RetrieveAttributeRequest
        {
            EntityLogicalName = entityLogicalName,
            LogicalName = ToLogicalName(attributeSchemaName),
            RetrieveAsIfPublished = false
        };

        var response = (RetrieveAttributeResponse)service.Execute(request);
        if (response?.AttributeMetadata?.MetadataId.HasValue == true)
        {
            AddComponentToSolution(service, options.SolutionName, "Attribute", response.AttributeMetadata.MetadataId.Value);
        }
    }

    private static void EnsureStringColumn(IOrganizationService service, string entityLogicalName, string schemaName, string displayName, int maxLength, bool required, SetupOptions options = null)
    {
        if (AttributeExists(service, entityLogicalName, schemaName))
        {
            EnsureAttributeInSolution(service, entityLogicalName, schemaName, options);
            return;
        }

        Console.WriteLine($"Creating string column: {entityLogicalName}.{schemaName}");
        var request = new CreateAttributeRequest
        {
            EntityName = entityLogicalName,
            Attribute = new StringAttributeMetadata
            {
                SchemaName = schemaName,
                DisplayName = Localized(displayName),
                MaxLength = maxLength,
                FormatName = StringFormatName.Text,
                RequiredLevel = Required(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None)
            }
        };
        service.Execute(request);
        EnsureAttributeInSolution(service, entityLogicalName, schemaName, options);
    }

    private static void EnsureMultilineColumn(IOrganizationService service, string entityLogicalName, string schemaName, string displayName, int maxLength, bool required, SetupOptions options = null)
    {
        if (AttributeExists(service, entityLogicalName, schemaName))
        {
            EnsureAttributeInSolution(service, entityLogicalName, schemaName, options);
            return;
        }

        Console.WriteLine($"Creating multiline column: {entityLogicalName}.{schemaName}");
        var request = new CreateAttributeRequest
        {
            EntityName = entityLogicalName,
            Attribute = new MemoAttributeMetadata
            {
                SchemaName = schemaName,
                DisplayName = Localized(displayName),
                MaxLength = maxLength,
                RequiredLevel = Required(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None)
            }
        };
        service.Execute(request);
        EnsureAttributeInSolution(service, entityLogicalName, schemaName, options);
    }

    private static void EnsureWholeNumberColumn(IOrganizationService service, string entityLogicalName, string schemaName, string displayName, bool required, int min, int max, SetupOptions options = null)
    {
        if (AttributeExists(service, entityLogicalName, schemaName))
        {
            EnsureAttributeInSolution(service, entityLogicalName, schemaName, options);
            return;
        }

        Console.WriteLine($"Creating whole number column: {entityLogicalName}.{schemaName}");
        var request = new CreateAttributeRequest
        {
            EntityName = entityLogicalName,
            Attribute = new IntegerAttributeMetadata
            {
                SchemaName = schemaName,
                DisplayName = Localized(displayName),
                RequiredLevel = Required(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None),
                MinValue = min,
                MaxValue = max,
                Format = IntegerFormat.None
            }
        };
        service.Execute(request);
        EnsureAttributeInSolution(service, entityLogicalName, schemaName, options);
    }

    private static void EnsureDateTimeColumn(IOrganizationService service, string entityLogicalName, string schemaName, string displayName, bool required, SetupOptions options = null)
    {
        if (AttributeExists(service, entityLogicalName, schemaName))
        {
            EnsureAttributeInSolution(service, entityLogicalName, schemaName, options);
            return;
        }

        Console.WriteLine($"Creating datetime column: {entityLogicalName}.{schemaName}");
        var request = new CreateAttributeRequest
        {
            EntityName = entityLogicalName,
            Attribute = new DateTimeAttributeMetadata
            {
                SchemaName = schemaName,
                DisplayName = Localized(displayName),
                RequiredLevel = Required(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None),
                Format = DateTimeFormat.DateAndTime,
                ImeMode = ImeMode.Disabled
            }
        };
        service.Execute(request);
        EnsureAttributeInSolution(service, entityLogicalName, schemaName, options);
    }

    private static void EnsureBooleanColumn(IOrganizationService service, string entityLogicalName, string schemaName, string displayName, bool required, string falseLabel, string trueLabel, SetupOptions options = null)
    {
        if (AttributeExists(service, entityLogicalName, schemaName))
        {
            EnsureAttributeInSolution(service, entityLogicalName, schemaName, options);
            return;
        }

        Console.WriteLine($"Creating boolean column: {entityLogicalName}.{schemaName}");
        var request = new CreateAttributeRequest
        {
            EntityName = entityLogicalName,
            Attribute = new BooleanAttributeMetadata
            {
                SchemaName = schemaName,
                DisplayName = Localized(displayName),
                RequiredLevel = Required(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None),
                OptionSet = new BooleanOptionSetMetadata(
                    new OptionMetadata(Localized(falseLabel), 0),
                    new OptionMetadata(Localized(trueLabel), 1))
            }
        };
        service.Execute(request);
        EnsureAttributeInSolution(service, entityLogicalName, schemaName, options);
    }

    private static void EnsureChoiceColumn(IOrganizationService service, string entityLogicalName, string schemaName, string displayName, bool required, IEnumerable<string> labels, SetupOptions options = null)
    {
        if (AttributeExists(service, entityLogicalName, schemaName))
        {
            EnsureAttributeInSolution(service, entityLogicalName, schemaName, options);
            return;
        }

        Console.WriteLine($"Creating choice column: {entityLogicalName}.{schemaName}");
        var optionValues = labels
            .Select((label, index) => new OptionMetadata(Localized(label), index + 1))
            .ToArray();

        var optionSetMetadata = new OptionSetMetadata
        {
            IsGlobal = false,
            OptionSetType = OptionSetType.Picklist
        };

        foreach (var option in optionValues)
        {
            optionSetMetadata.Options.Add(option);
        }

        var request = new CreateAttributeRequest
        {
            EntityName = entityLogicalName,
            Attribute = new PicklistAttributeMetadata
            {
                SchemaName = schemaName,
                DisplayName = Localized(displayName),
                RequiredLevel = Required(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None),
                OptionSet = optionSetMetadata
            }
        };

        service.Execute(request);
        EnsureAttributeInSolution(service, entityLogicalName, schemaName, options);
    }

    private static void EnsureBulkIngestionStateAndStatus(IOrganizationService service)
    {
        // statecode/statuscode are OOTB columns and already exist for custom tables.
        // Add business status reasons onto statuscode.
        EnsureStatusReason(service, BulkIngestionLogical, 0, "Draft");
        EnsureStatusReason(service, BulkIngestionLogical, 0, "Items Created");
        EnsureStatusReason(service, BulkIngestionLogical, 0, "Submitted");
        EnsureStatusReason(service, BulkIngestionLogical, 0, "Processing");
        EnsureStatusReason(service, BulkIngestionLogical, 0, "Partially Failed");

        EnsureStatusReason(service, BulkIngestionLogical, 1, "Completed");
        EnsureStatusReason(service, BulkIngestionLogical, 1, "Failed");
        EnsureStatusReason(service, BulkIngestionLogical, 1, "Cancelled");

        if (AttributeExists(service, BulkIngestionLogical, "voa_Status"))
        {
            Console.WriteLine("Note: legacy column voa_Status exists. OOTB statecode/statuscode is now the source of truth.");
        }
    }

    private static void EnsureStatusReason(IOrganizationService service, string entityLogicalName, int stateCode, string label)
    {
        var retrieveRequest = new RetrieveAttributeRequest
        {
            EntityLogicalName = entityLogicalName,
            LogicalName = "statuscode",
            RetrieveAsIfPublished = true
        };

        var retrieveResponse = (RetrieveAttributeResponse)service.Execute(retrieveRequest);
        if (retrieveResponse?.AttributeMetadata is not StatusAttributeMetadata statusMetadata)
        {
            Console.WriteLine($"Warning: Could not retrieve status metadata for {entityLogicalName}.");
            return;
        }

        var exists = statusMetadata.OptionSet?.Options?.Any(o =>
            string.Equals(o.Label?.UserLocalizedLabel?.Label, label, StringComparison.OrdinalIgnoreCase)) == true;
        if (exists)
        {
            return;
        }

        Console.WriteLine($"Creating status reason: {entityLogicalName}.{label} (state={stateCode})");
        var insertStatusValue = new InsertStatusValueRequest
        {
            EntityLogicalName = entityLogicalName,
            AttributeLogicalName = "statuscode",
            Label = Localized(label),
            StateCode = stateCode
        };

        service.Execute(insertStatusValue);
    }

    private static void EnsureLookupRelationship(IOrganizationService service, LookupRelationshipSpec spec)
    {
        if (!EntityExists(service, spec.ReferencedEntity))
        {
            Console.WriteLine($"Skipping lookup {spec.LookupSchemaName}: referenced entity '{spec.ReferencedEntity}' not found.");
            return;
        }

        if (AttributeExists(service, spec.ReferencingEntity, spec.LookupSchemaName))
        {
            return;
        }

        Console.WriteLine($"Creating lookup: {spec.ReferencingEntity}.{spec.LookupSchemaName} -> {spec.ReferencedEntity}");

        var request = new CreateOneToManyRequest
        {
            Lookup = new LookupAttributeMetadata
            {
                SchemaName = spec.LookupSchemaName,
                DisplayName = Localized(spec.LookupDisplayName),
                RequiredLevel = Required(AttributeRequiredLevel.None)
            },
            OneToManyRelationship = new OneToManyRelationshipMetadata
            {
                SchemaName = spec.RelationshipSchemaName,
                ReferencedEntity = spec.ReferencedEntity,
                ReferencingEntity = spec.ReferencingEntity,
                AssociatedMenuConfiguration = new AssociatedMenuConfiguration
                {
                    Behavior = AssociatedMenuBehavior.UseCollectionName,
                    Group = AssociatedMenuGroup.Details,
                    Label = Localized(spec.LookupDisplayName),
                    Order = 10000
                }
            }
        };

        service.Execute(request);
    }

    private static void EnsureLookupRelationship(IOrganizationService service, LookupRelationshipSpec spec, SetupOptions options = null)
    {
        EnsureLookupRelationship(service, spec);
        
        if (!string.IsNullOrWhiteSpace(options?.SolutionName) && AttributeExists(service, spec.ReferencingEntity, spec.LookupSchemaName))
        {
            var attrReq = new RetrieveAttributeRequest
            {
                EntityLogicalName = spec.ReferencingEntity,
                LogicalName = ToLogicalName(spec.LookupSchemaName),
                RetrieveAsIfPublished = false
            };
            var attrResp = (RetrieveAttributeResponse)service.Execute(attrReq);
            if (attrResp?.AttributeMetadata?.MetadataId.HasValue == true)
            {
                AddComponentToSolution(service, options.SolutionName, "Attribute", attrResp.AttributeMetadata.MetadataId.Value);
            }
        }
    }

    private static bool EntityExists(IOrganizationService service, string logicalName)
    {
        try
        {
            var request = new RetrieveEntityRequest
            {
                EntityFilters = EntityFilters.Entity,
                LogicalName = logicalName
            };

            service.Execute(request);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool AttributeExists(IOrganizationService service, string entityLogicalName, string attributeSchemaName)
    {
        try
        {
            var request = new RetrieveAttributeRequest
            {
                EntityLogicalName = entityLogicalName,
                LogicalName = ToLogicalName(attributeSchemaName),
                RetrieveAsIfPublished = true
            };

            service.Execute(request);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ToLogicalName(string schemaName)
    {
        // Dataverse schema names are PascalCase while logical names are lowercase.
        return schemaName.ToLowerInvariant();
    }

    private static Label Localized(string text)
    {
        return new Label(text, LcidEnUs);
    }

    private static AttributeRequiredLevelManagedProperty Required(AttributeRequiredLevel value)
    {
        return new AttributeRequiredLevelManagedProperty(value);
    }

    private sealed class SetupOptions
    {
        public string? EnvironmentUrl { get; set; }
        public string? ClientId { get; set; }
        public string? RedirectUri { get; set; }
        public string? RequestEntityLogicalName { get; set; } = "voa_requestlineitem";
        public string? JobEntityLogicalName { get; set; } = "incident";
        public string? SolutionName { get; set; } = "CTP_BulkData_Creation";
        public string? PublisherName { get; set; } = "voa";
        public string? PublisherPrefix { get; set; } = "voa";
        public bool Publish { get; set; } = true;
        public bool ColumnsOnly { get; set; }
    }

    private sealed record LookupRelationshipSpec(
        string ReferencedEntity,
        string ReferencingEntity,
        string LookupSchemaName,
        string LookupDisplayName,
        string RelationshipSchemaName);
}

