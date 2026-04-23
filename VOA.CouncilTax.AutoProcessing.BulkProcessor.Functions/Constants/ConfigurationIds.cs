using System;
using System.Collections.Generic;

namespace VOA.CouncilTax.AutoProcessing.Constants;

internal static class ConfigurationIds
{
    public static readonly Guid RequestTypeCouncilTax = new("63ea1cf3-cfd8-eb11-bacb-002248419a1d");
    public static readonly Guid CodedReasonNewProperty = new("c322ad67-412c-ec11-b6e6-002248432293");
    public static readonly Guid CodedReasonNewPropertyEstate = new("d7d2e987-a9eb-ee11-a1fd-002248c85ea4");
    public static readonly Guid CodedReasonReEntryNewProperty = new("4b70bade-3e48-ef11-a316-000d3a0ce7f2");

    public static readonly int EstateFileBlockedStatus = 589160000;
    public static readonly int HouseTypeApprovedStatus = 589160001;

    public static readonly Guid DomesticCompartmentPropertyAttributesTemplateId = new("19bfb577-658c-c74d-a178-b8d7cc98564c");
    public static readonly Guid AgeCodeNonDefiningAttributeId = new("b2de447a-440d-7c4f-f592-4af9b1714621");
    public static readonly Guid ReasonCodeNonDefiningAttributeId = new("e7c6b1e4-e8ba-d742-4464-0e2d9345a050");
    public static readonly Guid EffectiveDateNonDefiningAttributeId = new("594992dd-1bef-a838-5dfa-e7bccf907d30");
    public static readonly Guid FloorLevelNonDefiningAttributeId = new("447d93ad-a66e-ff96-7152-be491ebfc5ec");

    public static readonly Guid CodedReasonConsequentialBandChange = new("71512666-9018-ed11-b83f-002248428304");
    public static readonly Guid CodedReasonConsequentialBandReview = new("11265cca-9018-ed11-b83f-002248428304");
    public static readonly Guid CodedReasonAppeal = new("8caa7145-ce91-ed11-aad1-002248428304");
    public static readonly Guid CodedReasonProposal = new("b676852a-0690-ed11-aad1-0022484283ba");
    public static readonly Guid CodedReasonChangeOfBAReference = new("911f39b4-317f-ee11-8179-6045bd0c1c1b");
    public static readonly Guid CodedReasonEffectiveDateChangeReference = new("f22adfe6-6a93-ed11-aad1-002248428304");

    public static readonly List<Guid> ConsequentialJobTypes = new()
    {
        CodedReasonConsequentialBandChange,
        CodedReasonConsequentialBandReview,
        CodedReasonAppeal,
    };

    public static readonly Guid TemplateGroupCTInformalChallengeNoAction = new("97c7ff98-1ef7-ee11-a1ff-0022481b5fed");
    public static readonly Guid ReasonCodeValidatedId = new("ccd361ac-64f2-0ce1-fecf-e2598af9c2b3");
    public static readonly Guid DecisionIndicatorNoAction = new("4ad86410-022f-ef11-840a-002248c61497");

    public static readonly Guid IntegrationDataSourceCustomer = new("f592adee-f5f7-ee11-a1fe-002248c77263");
    public static readonly Guid IntegrationDataSourceBillingAuthority = new("41a9b8f4-f5f7-ee11-a1fe-6045bd0e5c5f");

    public static readonly Guid D2DeveloperEstateFilesSourceCodeId = new("b66c7bfb-9d09-ed11-82e5-6045bd0e7488");
    public static readonly Guid SharedLifeCycleActiveStatusId = new("1064b19a-7c00-ee11-8f6c-002248c727ce");
    public static readonly Guid SharedLifeCycleProposedStatusId = new("b4d1677c-80b7-ed11-6596-00224801fbb2");
    public static readonly Guid UnpublishedStatusId = new("6e1e26fb-8219-ee11-8f6d-082248c7287d");

    public static readonly int RequestInProgressStatusCode = 1;

    public static readonly Guid CouncilTaxBusinessProcessFlowId = new("a43ca2a4-225a-ef11-bfe3-000d3a0ce7f2");
    public const string CouncilTaxBusinessProcessFlowName = "voa_ctprocess";

    public static readonly Guid CouncilTaxBpfPublishStage = new("da8ad11e-a8b3-48df-bcaf-f8b43b7bcaac");
    public static readonly Guid AssessmentChangeCodeNewHereditament = new("83224758-5155-ed11-9562-6045bd0e7a95");
    public static readonly Guid BandingStatusCurrentLiveEntry = new("fead4e8c-8319-ee11-8f6d-002248c7287d");

    public static readonly List<Guid> ReleaseAndPublishStageIds = new()
    {
        new Guid("0240e965-113f-4319-aba5-0371c64493c8"),
        new Guid("21fd1ad6-2715-4bb4-9c43-06bf89eb1080"),
        new Guid("3c5864a9-01a7-4437-bb2e-20cb4bb04095"),
        new Guid("e9288240-4660-4514-95c1-5340a17acf03"),
        new Guid("c2fa7bb9-2f82-4369-8899-71008337774f"),
        new Guid("db4b8c3e-45a3-498e-b490-cc0a1cc1d18f"),
        new Guid("463fe41e-5a08-4332-a850-d003234fe55e"),
        new Guid("1b29dc58-cfe8-4d06-89dd-e84f7b428ed3"),
        new Guid("a5adfb25-c18c-4783-accf-fe5081898c1f"),
        new Guid("2a073bf2-70c9-411f-b45a-cf4698c09dae"),
        new Guid("fa645756-ac70-4257-6566-dfb0c3f6ce16"),
    };

    public static readonly int YesValue = 184360000;
    public static readonly int NoValue = 184360001;
    public static readonly int RequestStatusCodeOnHold = 589160001;

    public static readonly string QualityAssuranceRules = "voa_EvaluateIncidentQualityAssuranceRequirement";
    public static readonly string QualityControlRules = "voa_EvaluateIncidentQualityControlRequirement";
    public static readonly string CMFLJobTriggerCaseRouting = "voa_VOAAutoRouteJobwebhookURL";
    public static readonly string ReleaseAndPublishRules = "voa_StartJobResolution";

    public static readonly Guid CodedReasonChangeOfAddress = new("d722ad67-412c-ec11-b6e6-002248432293");
    public static readonly Guid Appeal = new("8caa7145-ce91-ed11-aad1-002248428304");
    public static readonly Guid ConsequentialBandReview = new("11265cca-9018-ed11-b83f-002248428304");
    public static readonly Guid ConsequentialBandChange = new("715126b6-9018-ed11-b83f-002248428304");

    public static readonly Guid CouncilTaxChangeOfAddressPublishStage = new("1b29dc58-cfe8-4d06-89dd-e84f7b428ed3");
    public static readonly Guid CouncilTaxChangeOfAddressDesktopResearchStage = new("9a76c8f4-2344-4422-8484-8559d156370b");

    public static readonly Guid NDAActionTypeProposed = new("f2d6d1ed-a85b-ee11-8def-000d3a86c49a");
    public static readonly Guid NDAActionTypePending = new("559accbd-a85b-ee11-8def-000d3a86c49a");
    public static readonly Guid NDAActionTypeCommitted = new("ea2b8fb7-a85b-ee11-8def-000d3a86c49a");

    public static readonly Guid CouncilTaxReEntryResearchingStage = new("292ca16d-f48e-4996-ac15-c7d9645f34ab");
    public static readonly Guid CouncilTaxReEntryBandingStage = new("3989f8ac-2675-4fd2-9843-cd46893571ae");
    public static readonly Guid CouncilTaxReEntryMaintainAssessmentStage = new("928455c3-72ef-4693-b5c5-68a5de8b13b5");

    public static readonly Guid LookupValuePreserveChange = new("cc07e845-0528-ef11-840a-0022481a9a5a");
    public static readonly Guid LookupValueCommitChange = new("2dd5863f-0528-ef11-840a-0022481a9a5a");
    public static readonly Guid LookupValueRollbackChange = new("16bbd752-0528-ef11-8402-0022481a9a5a");
    public static readonly Guid LookupValueRollbackAddress = new("da65db5e-0528-ef11-840a-0022481a9a5a");
    public static readonly Guid LookupValueRollbackPADS = new("b7f7666b-0528-ef11-840a-0022481a9a5a");

    public static readonly int RequestLineItemStatusReasonNoAction = 589160003;
    public static readonly int RequestLineItemStatusReasonListUpdated = 589160007;
    public static readonly int RequestLineItemStatusReasonResolved = 2;

    public const string CMUseCustomAPIforCaseRouting = "voa_CM_UseCustomAPIforJobRouting";

    public static readonly Guid CodedReasonMaterialIncrease = new("f435f15b-3337-ec11-8c64-000d3a0af4cd");
    public static readonly Guid CouncilTaxListTypeId = new("3f021348-f35d-ec11-8f8f-000d3ad65747");

    // TODO: The original copied file contained additional entries with malformed identifiers/GUIDs.
    // Add them back once validated from a trusted source file.
}
