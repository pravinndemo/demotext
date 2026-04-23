using System.Text.Json.Serialization;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Models;

/// <summary>
/// Request model for creating bulk ingestion items from a bulk ingestion record.
/// </summary>
public class CreateBulkIngestionItems
{
    public Guid BulkIngestionId { get; set; }

    public string Hereditaments { get; set; } = string.Empty;
}

