using CSVProssessor.Domain.Enums;

namespace CSVProssessor.Domain.Entities
{
    public class CsvJob : BaseEntity
    {
        /// <summary>
        /// Unique file name stored in MinIO (with timestamp and GUID)
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Original file name uploaded by user
        /// </summary>
        public string? OriginalFileName { get; set; }

        public CsvJobType Type { get; set; }
        public CsvJobStatus Status { get; set; }
    }
}