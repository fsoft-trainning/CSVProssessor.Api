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

        [HttpGet("list")]
        public async Task<IActionResult> ListAllCsvFilesAsync()
        {
            try
            {
                var result = await _csvService.ListAllCsvFilesAsync();
                return Ok(ApiResult<object>.Success(result));
            }
            catch (Exception ex)
            {
                // Handle exception
                var statusCode = ExceptionUtils.ExtractStatusCode(ex);
                var errorResponse = ExceptionUtils.CreateErrorResponse<object>(ex);
                return StatusCode(statusCode, errorResponse);
            }
        }

        [HttpGet("export/{fileName}")]
        public async Task<IActionResult> ExportSingleCsvFileAsync(string fileName)
        {
            try
            {
                var fileStream = await _csvService.ExportSingleCsvFileAsync(fileName);
                return File(fileStream, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                // Handle exception
                var statusCode = ExceptionUtils.ExtractStatusCode(ex);
                var errorResponse = ExceptionUtils.CreateErrorResponse<object>(ex);
                return StatusCode(statusCode, errorResponse);
            }
        }

        [HttpGet("export")]
        public async Task<IActionResult> ExportAllCsvFilesAsync()
        {
            try
            {
                var zipStream = await _csvService.ExportAllCsvFilesAsync();
                var fileName = $"csv_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
                return File(zipStream, "application/zip", fileName);
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