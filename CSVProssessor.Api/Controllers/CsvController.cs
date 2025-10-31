using CSVProssessor.Application.Interfaces;
using CSVProssessor.Application.Utils;
using Microsoft.AspNetCore.Mvc;

namespace CSVProssessor.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CsvController : ControllerBase
    {
        public readonly ICsvService _csvService;

        public CsvController(ICsvService csvService)
        {
            _csvService = csvService;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadCsvAsync(IFormFile file)
        {
            try
            {
                var result = await _csvService.ImportCsvAsync(file);
                return Accepted(ApiResult<object>.Success(result));
            }
            catch (Exception ex)
            {
                // Handle exception
                var statusCode = ExceptionUtils.ExtractStatusCode(ex);
                var errorResponse = ExceptionUtils.CreateErrorResponse<object>(ex);
                return StatusCode(statusCode, errorResponse);
            }
        }
    }
}