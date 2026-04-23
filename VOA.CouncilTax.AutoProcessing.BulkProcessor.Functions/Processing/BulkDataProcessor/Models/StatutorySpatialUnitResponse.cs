using System.Runtime.Serialization;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

/// <summary>
/// Represents a Statutory Spatial Unit (SSU) response from external systems.
/// Contains the SSU ID and related address information.
/// </summary>
[DataContract]
public class StatutorySpatialUnitResponse
{
    [DataMember(Name = "statutorySpatialUnitId", EmitDefaultValue = false)]
    public string? StatutorySpatialUnitId { get; set; }

    [DataMember(Name = "address", EmitDefaultValue = false)]
    public string? Address { get; set; }

    [DataMember(Name = "uniqueId", EmitDefaultValue = false)]
    public string? UniqueId { get; set; }
}

