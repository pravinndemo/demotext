namespace VOA.CouncilTax.AutoProcessing.Constants;

public static class ConfigurationValues
{
	public const string DataAccessLayerAssessmentAppName = "DataAccessLayerAssessmentApp";
	public const string DataAccessLayerPropertyAppName = "DataAccessLayer";

	public const int DelayedJobHistoryRecordRemarksLength = 200;
	public const int DelayedJobHistoryRecordDelayUntilInMinutes = 60;

	public const string IncidentEntityName = "incident";
	public const string IncidentId = "incidentid";
	public const string ReadyForQualityChecks = "voa_readyforqualitychecks";
	public const string JobType = "voa_codedreason";
	public const string NoActionConfirmed = "voa_noactionconfirmed";
	public const string ParentRequest = "voa_requestlineitemid";
	public const string Remarks = "voa_remarks";
	public const string ProposedBillingAuthority = "voa_proposedbillingauthorityid";
	public const string Owner = "ownerid";
	public const string ClosureDate = "voa_closuredate";
	public const string RequestTypeEntityName = "voa_requesttype";
}
