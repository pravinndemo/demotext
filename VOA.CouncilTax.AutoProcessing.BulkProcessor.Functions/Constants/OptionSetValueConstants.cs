using System.Collections.Generic;

namespace VOA.CouncilTax.AutoProcessing.Constants;

public static class OptionSetValueConstants
{
	public const int JobTypeReleaseActionReleaseNewValue = 358800000;
	public const int JobTypeReleaseActionReleaseAmendValue = 358800001;
	public const int JobTypeReleaseActionReleaseDeleteValue = 358800002;
	public const int JobTypeReleaseActionReleaseSimpleValue = 589160001;
	public const int JobTypeReleaseActionReleasePADsValue = 589160002;
	public const int JobTypeReleaseActionBARefNumberChangeValue = 589160003;
	public const int JobTypeReleaseActionAddressChangeValue = 589160004;

	public const int DelayedJobHistoryRecordReasonErrorValue = 589160001;
	public const int DelayedJobHistoryRecordReasonMandatedDelayValue = 589160000;
	public const int DelayedJobHistoryRecordReasonWaitingForOtherJobsValue = 589160002;

	public static readonly List<int> ReleaseActionsThatRequireAssessment = new()
	{
		JobTypeReleaseActionReleaseNewValue,
		JobTypeReleaseActionReleaseAmendValue,
		JobTypeReleaseActionReleaseDeleteValue,
	};

	public static readonly List<int> ReleaseActionsThatRequireJobResolveStatusOfListUpdated = new()
	{
		JobTypeReleaseActionReleaseNewValue,
		JobTypeReleaseActionReleaseAmendValue,
		JobTypeReleaseActionReleaseDeleteValue,
	};

	public const int NDAActionsReleaseStatusDraftValue = 358800000;
	public const int NDAActionsReleaseStatusValidatedValue = 358800001;
	public const int NDAActionsReleaseStatusReleaseValue = 358800002;
	public const int NDAActionsReleaseStatusNoActionValue = 358800003;
	public const int NDAActionsReleaseStatusErrorValue = 358800004;

	public const int IncidentResolutionStatecodeCompleted = 1;
	public const int IncidentResolutionStatuscodeClosed = 2;
	public const int IncidentStatuscodeResolved = 5;
	public const int IncidentStatuscodeListUpdated = 589160002;

	public const int ProposedAddressStatusReasonReleased = 589160006;
	public const int ProposedAddressStatusReasonReleaseFailed = 589160007;

	public const int NADStatusReasonActive = 1;
	public const int NADStatusReasonInactive = 2;
}
