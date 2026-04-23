using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Services;

/// <summary>
/// Service to parse CSV files stored in Dataverse file columns.
/// Extracts SSU IDs from CSV records.
/// </summary>
public sealed class CsvFileParser
{
    private readonly IOrganizationServiceAsync2 _dataverseService;
    private readonly ILogger _logger;

    public CsvFileParser(IOrganizationServiceAsync2 dataverseService, ILogger logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
    }

    /// <summary>
    /// Reads and parses a CSV file from a Dataverse file column.
    /// Returns list of (SSU ID, row number) records for creating bulk items.
    /// </summary>
    public Task<List<CsvRowRecord>> ParseCsvFromDataverseFileAsync(
        Guid bulkProcessorId,
        string bulkProcessorEntityName,
        string fileColumnName)
    {
        if (string.IsNullOrWhiteSpace(bulkProcessorEntityName))
        {
            throw new ArgumentException("Entity logical name cannot be null or empty.", nameof(bulkProcessorEntityName));
        }

        if (string.IsNullOrWhiteSpace(fileColumnName))
        {
            throw new ArgumentException("File column name cannot be null or empty.", nameof(fileColumnName));
        }

        var records = new List<CsvRowRecord>();

        try
        {
            _logger.LogInformation(
                "Reading CSV file from {EntityName}.{FileColumnName} for bulk processor {BulkProcessorId}",
                bulkProcessorEntityName, fileColumnName, bulkProcessorId);

            // Retrieve the file column from the bulk processor record
            var bulkProcessor = _dataverseService.Retrieve(
                bulkProcessorEntityName,
                bulkProcessorId,
                new Microsoft.Xrm.Sdk.Query.ColumnSet(fileColumnName));

            // Get file content from FileAttribute
            var fileAttribute = bulkProcessor.GetAttributeValue<object>(fileColumnName);
            if (fileAttribute is null)
            {
                _logger.LogWarning(
                    "File column {FileColumnName} is empty or null on bulk processor {BulkProcessorId}",
                    fileColumnName, bulkProcessorId);
                return Task.FromResult(records);
            }

            string csvContent;

            // Handle file content - could be string or file reference
            if (fileAttribute is string fileString)
            {
                csvContent = fileString;
            }
            else
            {
                // For other file types, attempt toString conversion
                csvContent = fileAttribute.ToString() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(csvContent))
            {
                _logger.LogWarning(
                    "CSV file content is empty or could not be read for bulk processor {BulkProcessorId}",
                    bulkProcessorId);
                return Task.FromResult(records);
            }

            records = ParseCsvContent(csvContent, bulkProcessorId);
            _logger.LogInformation(
                "Parsed {RecordCount} rows from CSV for bulk processor {BulkProcessorId}",
                records.Count, bulkProcessorId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error reading CSV file from {FileColumnName} on bulk processor {BulkProcessorId}",
                fileColumnName, bulkProcessorId);
            throw;
        }

        return Task.FromResult(records);
    }

    /// <summary>
    /// Parses CSV content (assumes simple CSV with single column: SSU IDs only).
    /// Expected format (with or without header):
    /// ssuId
    /// 550e8400-e29b-41d4-a716-446655440000
    /// 550e8400-e29b-41d4-a716-446655440001
    /// </summary>
    private List<CsvRowRecord> ParseCsvContent(string csvContent, Guid bulkProcessorId)
    {
        var records = new List<CsvRowRecord>();
        var lines = csvContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        if (lines.Length == 0)
        {
            _logger.LogWarning("CSV file has no content for bulk processor {BulkProcessorId}", bulkProcessorId);
            return records;
        }

        // Simple: assume first line might be header or data
        // Try to detect header by checking if first cell looks like "ssuId" or "ssuid"
        var startRow = 0;
        var firstLine = lines[0].Trim();

        // Common CSV headers to detect
        if (firstLine.Equals("ssuId", StringComparison.OrdinalIgnoreCase) ||
            firstLine.Equals("ssuid", StringComparison.OrdinalIgnoreCase) ||
            firstLine.Equals("SSU ID", StringComparison.OrdinalIgnoreCase) ||
            firstLine.Equals("SSU", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(firstLine, out _)) // If it's not a GUID, treat as header
        {
            startRow = 1;
            _logger.LogInformation("Detected CSV header row. Starting data parse from row {StartRow}", startRow + 1);
        }

        var rowNumber = startRow + 1;
        for (var i = startRow; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line))
            {
                rowNumber++;
                continue;
            }

            var ssuId = line; // Single column: just the SSU ID

            if (string.IsNullOrWhiteSpace(ssuId))
            {
                _logger.LogWarning(
                    "CSV row {RowNumber} is empty. Skipping.",
                    rowNumber);
                rowNumber++;
                continue;
            }

            records.Add(new CsvRowRecord
            {
                SsuId = ssuId,
                SourceRowNumber = rowNumber,
            });

            rowNumber++;
        }

        _logger.LogInformation(
            "Parsed {SuccessCount} valid SSU IDs from CSV for bulk processor {BulkProcessorId}",
            records.Count, bulkProcessorId);

        return records;
    }
}

/// <summary>
/// Represents a single row parsed from CSV: SSU ID + row number.
/// </summary>
public sealed class CsvRowRecord
{
    public string SsuId { get; set; } = string.Empty;

    public int SourceRowNumber { get; set; }
}
