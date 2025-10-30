using CSVProssessor.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace CSVProssessor.Api.Controllers
{
    /// <summary>
    /// CSV processing controller for import and export operations.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class CsvController : ControllerBase
    {
        private readonly ICsvService _csvService;
        private readonly ILogger<CsvController> _logger;

        public CsvController(ICsvService csvService, ILogger<CsvController> logger)
        {
            _csvService = csvService;
            _logger = logger;
        }

        /// <summary>
        /// Import CSV file asynchronously.
        /// File is uploaded to blob storage and a message is published to RabbitMQ queue.
        /// Background service will process the import asynchronously.
        /// </summary>
        /// <param name="file">The CSV file to import.</param>
        /// <returns>202 Accepted with job ID for tracking.</returns>
        /// <response code="202">Job accepted for processing. Use jobId to track status.</response>
        /// <response code="400">File is missing or invalid.</response>
        /// <response code="500">Internal server error during upload or message publishing.</response>
        [HttpPost("import")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ImportCsvResponse), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ImportCsv(IFormFile file)
        {
            try
            {
                // Validate file
                if (file == null || file.Length == 0)
                {
                    _logger.LogWarning("Import CSV request with missing or empty file");
                    return BadRequest(new { error = "File is required and cannot be empty." });
                }

                // Check file extension
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (extension != ".csv")
                {
                    _logger.LogWarning($"Import CSV request with invalid file type: {extension}");
                    return BadRequest(new { error = "Only CSV files are accepted." });
                }

                _logger.LogInformation($"Starting CSV import for file: {file.FileName}");

                // Call service to handle import asynchronously
                using var stream = file.OpenReadStream();
                var jobId = await _csvService.ImportCsvAsync(stream, file.FileName);

                _logger.LogInformation($"CSV import job created with ID: {jobId}");

                // Return 202 Accepted with job ID
                return Accepted(new ImportCsvResponse
                {
                    JobId = jobId,
                    FileName = file.FileName,
                    Message = "File received and queued for processing. Check status using the job ID."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during CSV import: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new { error = "An error occurred during file upload.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Export data as CSV file.
        /// Queries data from database, generates CSV, uploads to blob storage and returns download URL.
        /// </summary>
        /// <param name="exportFileName">Optional custom name for the exported file. If not provided, a default name will be generated.</param>
        /// <returns>200 OK with presigned URL for downloading the CSV file.</returns>
        /// <response code="200">Export successful. Contains presigned URL for download.</response>
        /// <response code="500">Internal server error during export process.</response>
        [HttpPost("export")]
        [ProducesResponseType(typeof(ExportCsvResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ExportCsv([FromQuery] string? exportFileName = null)
        {
            try
            {
                // Generate default filename if not provided
                if (string.IsNullOrWhiteSpace(exportFileName))
                {
                    exportFileName = $"export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
                }

                _logger.LogInformation($"Starting CSV export with filename: {exportFileName}");

                // Call service to handle export
                var presignedUrl = await _csvService.ExportCsvAsync(exportFileName);

                _logger.LogInformation($"CSV export completed successfully: {exportFileName}");

                // Return 200 OK with download URL
                return Ok(new ExportCsvResponse
                {
                    FileName = exportFileName,
                    DownloadUrl = presignedUrl,
                    ExportedAt = DateTime.UtcNow,
                    Message = "Export completed successfully. Use the download URL to fetch the file."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during CSV export: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new { error = "An error occurred during export.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Health check endpoint for CSV service.
        /// </summary>
        /// <returns>200 OK with service status.</returns>
        [HttpGet("health")]
        [ProducesResponseType(typeof(HealthCheckResponse), StatusCodes.Status200OK)]
        public IActionResult Health()
        {
            return Ok(new HealthCheckResponse
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                Service = "CSV Service"
            });
        }
    }

    /// <summary>
    /// Response model for import CSV endpoint.
    /// </summary>
    public class ImportCsvResponse
    {
        /// <summary>
        /// Unique identifier for tracking the import job.
        /// </summary>
        public Guid JobId { get; set; }

        /// <summary>
        /// Name of the uploaded file.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Status message for the client.
        /// </summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// Response model for export CSV endpoint.
    /// </summary>
    public class ExportCsvResponse
    {
        /// <summary>
        /// Name of the exported file.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Presigned URL for downloading the exported CSV file.
        /// </summary>
        public string DownloadUrl { get; set; }

        /// <summary>
        /// Timestamp when export was completed.
        /// </summary>
        public DateTime ExportedAt { get; set; }

        /// <summary>
        /// Status message for the client.
        /// </summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// Response model for health check endpoint.
    /// </summary>
    public class HealthCheckResponse
    {
        /// <summary>
        /// Health status of the service.
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Timestamp of the health check.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Name of the service being checked.
        /// </summary>
        public string Service { get; set; }
    }
}
