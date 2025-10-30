namespace CSVProssessor.Application.Interfaces
{
    public interface ICsvService
    {

        Task<Guid> ImportCsvAsync(Stream fileStream, string fileName);

        Task<string> ExportCsvAsync(string exportFileName);
    }
}