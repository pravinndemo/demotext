using System.Text.Json.Serialization;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

/// <summary>
/// Request model for retrieving banding information for properties.
/// Used when requesting banding data from external systems.
/// </summary>
public class GetBandingsRequest
{
    [JsonPropertyName("subjectSsu")]
    public Guid SubjectSsu { get; set; }

    [JsonPropertyName("consequentialSsus")]
    public List<Guid> ConsequentialSsus { get; set; } = new();
}

