using CSVProssessor.Domain.Enums;

namespace CSVProssessor.Domain.Entities
{
    public class CsvJob
    {
        public Guid Id { get; set; }
        public string FileName { get; set; }
        public CsvJobType Type { get; set; }
        public DateTime CreatedAt { get; set; }
        public CsvJobStatus Status { get; set; }
    }
}
