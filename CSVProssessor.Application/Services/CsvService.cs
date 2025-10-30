using CSVProssessor.Application.Interfaces;
using CSVProssessor.Application.Interfaces.Common;
using CSVProssessor.Infrastructure.Interfaces;

namespace CSVProssessor.Application.Services
{
    public class CsvService : ICsvService
    {
        public readonly IUnitOfWork _unitOfWork;
        public readonly IBlobService _blobService;
        public CsvService(IUnitOfWork unitOfWork, IBlobService blobService)
        {
            _unitOfWork = unitOfWork;
            _blobService = blobService;
        }

        //import-csv method
        //export-csv method


    }
}
