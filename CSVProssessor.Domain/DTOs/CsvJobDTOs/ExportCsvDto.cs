namespace CSVProssessor.Domain.DTOs.CsvJobDTOs
{
    /// <summary>
    /// DTO for CSV file information
    /// </summary>
    public class CsvFileInfoDto
    {
        /// <summary>
        /// Unique name of the CSV file stored in MinIO
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Original file name uploaded by user
        /// </summary>
        public string? OriginalFileName { get; set; }

        /// <summary>
        /// Job ID associated with this file
        /// </summary>
        public Guid JobId { get; set; }

        /// <summary>
        /// Timestamp when file was uploaded
        /// </summary>
        public DateTime UploadedAt { get; set; }

        /// <summary>
        /// Processing status (Pending, Completed, Failed)
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Total number of records imported from this file
        /// </summary>
        public int RecordCount { get; set; }
    }

    /// <summary>
    /// DTO for a single exported CSV file with download URL
    /// </summary>
    public class ExportedFileDto
    {
        /// <summary>
        /// Name of the CSV file
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Presigned download URL from blob storage
        /// </summary>
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when file was uploaded
        /// </summary>
        public DateTime UploadedAt { get; set; }

        /// <summary>
        /// Processing status (Pending, Completed, Failed)
        /// </summary>
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response DTO for ListAllCsvFilesAsync
    /// Contains list of all uploaded CSV files with metadata
    /// </summary>
    public class ListCsvFilesResponseDto
    {
        /// <summary>
        /// Total number of files
        /// </summary>
        public int TotalFiles { get; set; }

        /// <summary>
        /// List of CSV file information
        /// </summary>
        public List<CsvFileInfoDto> Files { get; set; } = new List<CsvFileInfoDto>();

        /// <summary>
        /// Timestamp when list was generated
        /// </summary>
        public DateTime GeneratedAt { get; set; }

        /// <summary>
        /// Success message
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response DTO for ExportAllCsvFilesAsync
    /// Contains list of all uploaded CSV files with their download URLs
    /// </summary>
    public class ExportCsvResponseDto
    {
        /// <summary>
        /// Total number of exported files
        /// </summary>
        public int TotalFiles { get; set; }

        /// <summary>
        /// List of exported files with download URLs
        /// </summary>
        public List<ExportedFileDto> Files { get; set; } = new List<ExportedFileDto>();

        /// <summary>
        /// Timestamp when export was completed
        /// </summary>
        public DateTime ExportedAt { get; set; }

        /// <summary>
        /// Success message
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
