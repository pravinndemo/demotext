using System.Text.Json.Serialization;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

/// <summary>
/// Represents a Statutory Spatial Unit ID returned from data access layer.
/// </summary>
public class SsuIdItem
{
    [JsonPropertyName("statutorySpatialUnitId")]
    public string StatutorySpatialUnitId { get; set; } = string.Empty;

    [JsonPropertyName("addressString")]
    public string AddressString { get; set; } = string.Empty;
}

/// <summary>
/// Represents hereditament (property) attribute data from data access layer.
/// Contains banding, list, and valuation information for a property.
/// </summary>
public class DalHereditament
{
    [JsonPropertyName("statutorySpatialUnitId")]
    public string StatutorySpatialUnitId { get; set; } = string.Empty;

    [JsonPropertyName("propertyAttributeData")]
    public string PropertyAttributeData { get; set; } = string.Empty;

    [JsonPropertyName("bandId")]
    public string BandId { get; set; } = string.Empty;

    [JsonPropertyName("listId")]
    public string ListId { get; set; } = string.Empty;

    [JsonPropertyName("vtorHcDecisionId")]
    public string VtorHcDecisionId { get; set; } = string.Empty;

    [JsonPropertyName("isComposite")]
    public bool IsComposite { get; set; }

    [JsonPropertyName("improvementIndicator")]
    public bool ImprovementIndicator { get; set; }

    [JsonPropertyName("milesFromSubject")]
    public decimal? MilesFromSubject { get; set; }
}

