namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Services;

/// <summary>
/// FetchXML query builder for bulk ingestion Dataverse operations.
/// Provides parameterized FetchXML queries for retrieving bulk ingestion records and items.
/// </summary>
public static class FetcherXMLHelper
{
    /// <summary>
    /// Retrieves Statutory Spatial Unit (SSU) records matching optional filter criteria.
    /// </summary>
    /// <param name="ssuNameFilter">Optional filter by SSU name (uses 'like' operator). If null, retrieves all active SSUs.</param>
    public static string GetSsuQuery(string? ssuNameFilter = null)
    {
        string filterCondition = string.IsNullOrEmpty(ssuNameFilter)
            ? string.Empty
            : $@"
      <condition attribute='voa_name' operator='like' value='%{ssuNameFilter}%' />";

        return $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
  <entity name='voa_ssu'>
    <attribute name='voa_ssuid' />
    <attribute name='voa_name' />
    <attribute name='createdon' />
    <order attribute='voa_name' descending='false' />
    <filter type='and'>{filterCondition}
    </filter>
  </entity>
</fetch>";
    }

    /// <summary>
    /// Retrieves bulk ingestion items in Valid state for a specific parent bulk ingestion.
    /// </summary>
    /// <param name="bulkIngestionId">The ID of the parent bulk ingestion record.</param>
    public static string GetBulkIngestionItemsByStatus(string bulkIngestionId)
    {
        return $@"<fetch>
  <entity name='voa_bulkingestionitem'>
    <attribute name='voa_bulkingestionitemid' />
    <attribute name='voa_hereditament' />
    <attribute name='voa_parentbulkingestion' />
    <filter type='and'>
      <condition attribute='voa_validationstatus' operator='eq' value='358800001' />
      <condition attribute='voa_parentbulkingestion' operator='eq' value='{bulkIngestionId}' />
    </filter>
  </entity>
</fetch>";
    }

    /// <summary>
    /// Retrieves all bulk ingestions in Active state (statecode=0) with processing job type.
    /// </summary>
    public static string GetActiveBulkIngestions()
    {
        return @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
  <entity name='voa_bulkingestion'>
    <attribute name='voa_bulkingestionid' />
    <attribute name='voa_name' />
    <attribute name='createdon' />
    <attribute name='voa_processingjobtype' />
    <attribute name='statuscode' />
    <order attribute='voa_name' descending='false' />
    <filter type='and'>
      <condition attribute='statecode' operator='eq' value='0' />
    </filter>
  </entity>
</fetch>";
    }

    /// <summary>
    /// Retrieves a single bulk ingestion by specific status code.
    /// </summary>
    /// <param name="statusCode">The statuscode value to filter by (e.g., 358800003 for Queued).</param>
    public static string GetBulkIngestionByStatus(int statusCode)
    {
        return $@"<fetch>
  <entity name='voa_bulkingestion'>
    <attribute name='voa_bulkingestionid' />
    <filter>
      <condition attribute='statuscode' operator='eq' value='{statusCode}' />
      <condition attribute='statecode' operator='eq' value='0' />
    </filter>
  </entity>
</fetch>";
    }

    /// <summary>
    /// Retrieves bulk ingestion items in a specific validation state for a parent ingestion.
    /// </summary>
    /// <param name="bulkIngestionId">The ID of the parent bulk ingestion.</param>
    /// <param name="validationStatusCode">The validation status code (e.g., 358800001 for Valid).</param>
    public static string GetBulkIngestionItemsByValidationStatus(string bulkIngestionId, int validationStatusCode)
    {
        return $@"<fetch>
  <entity name='voa_bulkingestionitem'>
    <attribute name='voa_bulkingestionitemid' />
    <attribute name='voa_hereditament' />
    <attribute name='voa_parentbulkingestion' />
    <attribute name='voa_validationstatus' />
    <filter type='and'>
      <condition attribute='voa_validationstatus' operator='eq' value='{validationStatusCode}' />
      <condition attribute='voa_parentbulkingestion' operator='eq' value='{bulkIngestionId}' />
    </filter>
  </entity>
</fetch>";
    }

/// <summary>
/// Retrieves a bulk ingestion record by its ID.
/// </summary>
/// <param name="bulkIngestionId">The ID of the bulk ingestion record to retrieve.</param>
/// <returns>A FetchXML query string to retrieve the specified bulk ingestion record.</returns>
    public static string getBulkIngestionFromID(string bulkIngestionId)
    {
        string fetchXML = $@"<fetch>
    <entity name='voa_bulkingestion'>
    <attribute name='voa_bulkingestionid'/>
    <attribute name='voa_name'/>
    <attribute name='createdon'/>
    <attribute name='voa_assignedteam'/>
    <attribute name='voa_assignedmanager'/>
    <attribute name='voa_assignmentmode'/>
    <filter type='and'>
    <condition attribute='voa_bulkingestionid' operator='eq' value='{bulkIngestionId}'/>
    </filter>
    </entity>
    </fetch>";

        return fetchXML;
    }
}

