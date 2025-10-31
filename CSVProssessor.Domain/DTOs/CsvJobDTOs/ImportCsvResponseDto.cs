namespace CSVProssessor.Domain.DTOs.CsvJobDTOs
{
    public class ImportCsvResponseDto
    {
        public Guid JobId { get; set; }

        public string FileName { get; set; }

        public DateTime UploadedAt { get; set; }

        public string Status { get; set; }

        public string Message { get; set; }
    }
}
