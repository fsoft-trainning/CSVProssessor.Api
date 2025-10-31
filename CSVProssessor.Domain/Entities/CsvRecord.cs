using System.Text.Json;

namespace CSVProssessor.Domain.Entities
{
    public class CsvRecord : BaseEntity
    {
        public Guid JobId { get; set; }
        public string? FileName { get; set; }
        public DateTime ImportedAt { get; set; }
        public JsonDocument? Data { get; set; }
    }
}