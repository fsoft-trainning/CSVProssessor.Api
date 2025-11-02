namespace CSVProssessor.Application.Interfaces
{
    using CSVProssessor.Domain.DTOs.CsvJobDTOs;
    using Microsoft.AspNetCore.Http;

    public interface ICsvService
    {
        /// <summary>
        /// Import CSV asynchronously.
        /// Upload file to MinIO
        /// Create CsvJob record in database with Pending status
        /// Publish message to RabbitMQ queue for background processing
        /// </summary>
        Task<ImportCsvResponseDto> ImportCsvAsync(IFormFile file);

        /// <summary>
        /// Process CSV import: download file from MinIO, parse it, and save records to database
        /// Called by CsvImportQueueListenerService
        /// Handles: Download → Parse CSV → Save to DB → Update job status
        /// </summary>
        Task SaveCsvRecordsAsync(Guid jobId, string fileName);

        /// <summary>
        /// Detect changes and publish notification to RabbitMQ topic "csv-changes-topic"
        /// Called by ChangeDetectionBackgroundService to check for changes and notify all instances.
        /// </summary>
        Task<DetectChangesResponseDto> DetectAndPublishChangesAsync(DetectChangesRequestDto request);

        /// <summary>
        /// List all CSV files that have been uploaded to the system.
        /// Returns metadata about each file including name, status, upload time, and record count.
        /// </summary>
        Task<ListCsvFilesResponseDto> ListAllCsvFilesAsync();

        /// <summary>
        /// Export a specific CSV file by filename.
        /// Downloads the file from MinIO and returns it as a stream.
        /// </summary>
        /// <param name="fileName">Name of the CSV file to export</param>
        Task<Stream> ExportSingleCsvFileAsync(string fileName);

        /// <summary>
        /// Export all CSV files that have been uploaded to the system.
        /// Downloads files from MinIO and returns them as a zip archive stream.
        /// </summary>
        Task<Stream> ExportAllCsvFilesAsync();
    }
}