using Microsoft.AspNetCore.Mvc;

namespace CSVProssessor.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly ILogger<TestController> _logger;

        public TestController(ILogger<TestController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Simple health check endpoint
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            _logger.LogInformation("Health check endpoint called");
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                service = "CSVProcessor API",
                instance = Environment.GetEnvironmentVariable("INSTANCE_NAME") ?? "unknown"
            });
        }

        /// <summary>
        /// Echo endpoint - returns whatever you send
        /// </summary>
        [HttpPost("echo")]
        public IActionResult Echo([FromBody] object data)
        {
            _logger.LogInformation("Echo endpoint called");
            return Ok(new
            {
                received = data,
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Test GET with query parameters
        /// </summary>
        [HttpGet("params")]
        public IActionResult TestParams([FromQuery] string name, [FromQuery] int? age)
        {
            _logger.LogInformation($"Test params called with name={name}, age={age}");
            return Ok(new
            {
                name = name ?? "anonymous",
                age = age ?? 0,
                message = $"Hello {name ?? "anonymous"}, you are {age ?? 0} years old"
            });
        }

        /// <summary>
        /// Test POST with form data
        /// </summary>
        [HttpPost("form")]
        public IActionResult TestForm([FromForm] string field1, [FromForm] string field2)
        {
            _logger.LogInformation($"Test form called with field1={field1}, field2={field2}");
            return Ok(new
            {
                field1,
                field2,
                received = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Test file upload
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> TestUpload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded" });
            }

            _logger.LogInformation($"File uploaded: {file.FileName}, Size: {file.Length} bytes");

            using var reader = new StreamReader(file.OpenReadStream());
            var preview = await reader.ReadToEndAsync();
            var previewLength = Math.Min(200, preview.Length);

            return Ok(new
            {
                filename = file.FileName,
                contentType = file.ContentType,
                size = file.Length,
                preview = preview.Substring(0, previewLength),
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Test error response
        /// </summary>
        [HttpGet("error")]
        public IActionResult TestError([FromQuery] int statusCode = 500)
        {
            _logger.LogWarning($"Test error endpoint called with status code {statusCode}");
            return StatusCode(statusCode, new
            {
                error = $"Test error with status code {statusCode}",
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Test async delay
        /// </summary>
        [HttpGet("delay")]
        public async Task<IActionResult> TestDelay([FromQuery] int seconds = 1)
        {
            _logger.LogInformation($"Test delay called with {seconds} seconds");
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(seconds, 30)));
            return Ok(new
            {
                delayed = seconds,
                message = $"Delayed for {seconds} seconds",
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Test environment variables
        /// </summary>
        [HttpGet("env")]
        public IActionResult TestEnvironment()
        {
            _logger.LogInformation("Environment test endpoint called");
            return Ok(new
            {
                environment = new
                {
                    instanceName = Environment.GetEnvironmentVariable("INSTANCE_NAME"),
                    serviceType = Environment.GetEnvironmentVariable("SERVICE_TYPE"),
                    rabbitMqHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST"),
                    minioEndpoint = Environment.GetEnvironmentVariable("MINIO_ENDPOINT"),
                    aspnetcoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                },
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Test random data generation
        /// </summary>
        [HttpGet("random")]
        public IActionResult TestRandom([FromQuery] int count = 5)
        {
            _logger.LogInformation($"Random data generation called with count={count}");
            var data = Enumerable.Range(1, Math.Min(count, 100))
                .Select(i => new
                {
                    id = i,
                    randomString = Guid.NewGuid().ToString(),
                    randomNumber = Random.Shared.Next(1, 1000),
                    randomBool = Random.Shared.Next(0, 2) == 1
                })
                .ToList();

            return Ok(new
            {
                count = data.Count,
                data,
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Test different HTTP methods
        /// </summary>
        [HttpPut("update/{id}")]
        public IActionResult TestUpdate(int id, [FromBody] object data)
        {
            _logger.LogInformation($"Update endpoint called for ID {id}");
            return Ok(new
            {
                id,
                data,
                message = "Resource updated",
                timestamp = DateTime.UtcNow
            });
        }

        [HttpDelete("delete/{id}")]
        public IActionResult TestDelete(int id)
        {
            _logger.LogInformation($"Delete endpoint called for ID {id}");
            return Ok(new
            {
                id,
                message = "Resource deleted",
                timestamp = DateTime.UtcNow
            });
        }

        [HttpPatch("patch/{id}")]
        public IActionResult TestPatch(int id, [FromBody] object data)
        {
            _logger.LogInformation($"Patch endpoint called for ID {id}");
            return Ok(new
            {
                id,
                data,
                message = "Resource patched",
                timestamp = DateTime.UtcNow
            });
        }
    }
}
