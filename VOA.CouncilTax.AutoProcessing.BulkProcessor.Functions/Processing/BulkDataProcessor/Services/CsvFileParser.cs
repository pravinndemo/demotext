using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace VOA.CouncilTax.AutoProcessing.BulkProcessor.Functions.Processing.BulkDataProcessor.Services
{
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

        private async Task<byte[]> DownloadFileAsync(EntityReference entityReference, string fileFieldName)
        {
            var request = new InitializeFileBlocksDownloadRequest
            {
                Target = entityReference,
                FileAttributeName = fileFieldName
            };

            var response = (InitializeFileBlocksDownloadResponse)
                await _dataverseService.ExecuteAsync(request);

            var downloadRequest = new DownloadBlockRequest
            {
                FileContinuationToken = response.FileContinuationToken,
                BlockLength = response.FileSizeInBytes,
                Offset = 0
            };

            var downloadResponse = (DownloadBlockResponse)
                await _dataverseService.ExecuteAsync(downloadRequest);

            return downloadResponse.Data;
        }

        public async Task<List<CsvRowRecord>> RetriveSsuIdFromFile(
            Guid bulkProcessorId,
            string bulkProcessorEntityName,
            string fileColumnName)
        {
            var bulkIngestionReference = new EntityReference(bulkProcessorEntityName, bulkProcessorId);
            var records = new List<CsvRowRecord>();

            _logger.LogInformation("bulkIngestionReference.Id is not null");

            byte[] fileContent = await DownloadFileAsync(bulkIngestionReference, fileColumnName);

            if (fileContent == null)
                return null;

            string csvData = Encoding.UTF8.GetString(fileContent);

            _logger.LogInformation("File Content Loaded");

            string[] lines = csvData.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.RemoveEmptyEntries);

            int startRow = 0;
            int rowNumber = startRow + 1;

            foreach (string line in lines)
            {
                string ssuId = line.Trim();

                // Skip header
                if (ssuId.Equals("SSUID", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line))
                {
                    rowNumber++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(ssuId))
                {
                    _logger.LogWarning(
                        $"CSV row {rowNumber} is empty. Skipping.");

                    rowNumber++;
                    continue;
                }

                if (Guid.TryParse(ssuId, out Guid validGuid))
                {
                    records.Add(new CsvRowRecord
                    {
                        SsuId = ssuId,
                        SourceRowNumber = rowNumber
                    });
                }

                rowNumber++;
            }

            return records;
        }

        // Backward-compatible alias for callers using the newer method name.
        public Task<List<CsvRowRecord>> ParseCsvFromDataverseFileAsync(
            Guid bulkProcessorId,
            string bulkProcessorEntityName,
            string fileColumnName)
        {
            return RetriveSsuIdFromFile(bulkProcessorId, bulkProcessorEntityName, fileColumnName);
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
}