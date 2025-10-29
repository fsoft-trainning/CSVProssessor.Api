# CSV Processor - AI Coding Agent Instructions

## Project Overview

**CSVProssessor** is a distributed CSV processing system with:
- **2 WebAPI instances** (api-1, api-2) on ports 5001/5002 - handle HTTP requests + queue/topic listening via `IHostedService`
- **PostgreSQL** (port 5432) - primary database
- **RabbitMQ** (port 5672) - message broker for queue & topic communication
- **MinIO S3** (port 9000) - object storage for CSV files
- **Architecture**: .NET 8.0, layered (Api → Application → Domain → Infrastructure)

## Critical Data Flow

```
1. IMPORT: Actor → API (POST /api/csv-import) 
   → Upload CSV to MinIO → Send message to RabbitMQ Queue
   
2. QUEUE PROCESSING: api-1/api-2 BackgroundService 
   → Listen Queue → Download from MinIO → Write to PostgreSQL
   
3. EXPORT: Actor → API (POST /api/csv-export) 
   → Query PostgreSQL → Upload to MinIO → Return SAS URL
   
4. TIMER JOB: api-1/api-2 BackgroundService (every 5 min)
   → Read DB changes → Send to RabbitMQ Topic → Log notification
```

## Project Structure & Key Files

| Layer | Path | Purpose |
|-------|------|---------|
| **API** | `CSVProssessor.Api/Program.cs` | Entry point; enables Swagger, CORS, JWT |
| **DI Setup** | `CSVProssessor.Api/Architecture/IOContainer.cs` | Service registration for DbContext, Swagger, repositories |
| **Domain** | `CSVProssessor.Domain/` | Entity models, `AppDbContext.cs` |
| **Infrastructure** | `CSVProssessor.Infrastructure/` | `UnitOfWork`, Repository pattern, database utilities |
| **Controllers** | `CSVProssessor.Api/Controllers/` | API endpoints (import, export, health) |
| **Configuration** | `appsettings.json`, `docker-compose.yml` | Connection strings, service credentials |

## Critical Environment Variables (docker-compose.yml)

```yaml
DB_CONNECTION_STRING: "Server=postgres;Port=5432;Database=csvprossessor_db;User Id=postgres;Password=postgres;"
MINIO_ENDPOINT: "http://minio:9000"
MINIO_ACCESS_KEY: "minioadmin"
MINIO_SECRET_KEY: "minioadmin"
RABBITMQ_HOST: "rabbitmq"
RABBITMQ_USER: "guest"
RABBITMQ_PASSWORD: "guest"
RABBITMQ_QUEUE_NAME: "csv-import-queue"
RABBITMQ_TOPIC_NAME: "csv-changes-topic"
SERVICE_TYPE: "ApiWithQueueListener"  # Both api-1 and api-2
INSTANCE_NAME: "api-1" or "api-2"    # For logging distinction
```

## Service Registration Pattern

Services registered in `IOContainer.cs`:
- `IUnitOfWork` → `UnitOfWork` (scoped) - database operations
- `IGenericRepository<>` → `GenericRepository<>` (scoped) - CRUD pattern
- `IClaimsService`, `ICurrentTime`, `ILoggerService` - business layer

**When adding new services**: Always register in `SetupBusinessServicesLayer()` and inject via constructor, never `ServiceLocator.GetService()`.

## Hosted Services Pattern

Both API instances run two background services:

```csharp
// 1. Queue Listener (continuous)
public class CsvImportQueueListenerService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Listen to csv-import-queue indefinitely
    }
}

// 2. Timer Job (every 5 minutes)
public class ChangeDetectionBackgroundService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Check DB for changes
            // Publish to csv-changes-topic
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

Register in `Program.cs`:
```csharp
services.AddHostedService<CsvImportQueueListenerService>();
services.AddHostedService<ChangeDetectionBackgroundService>();
```

## API Endpoint Patterns

| Endpoint | Method | Request | Response |
|----------|--------|---------|----------|
| `/api/csv-export` | POST | JSON query | 200 OK + MinIO file URL |
| `/api/csv-import` | POST | FormFile (CSV) | 202 Accepted + jobId |
| `/api/health` | GET | - | 200 OK + service status |

**Key**: Import returns 202 (async processing), export returns 200 (sync or quick).

## Development Workflows

### Local Docker Deployment
```powershell
# Remove old containers if needed
docker rm -f csvprossessor_postgres csvprossessor_rabbitmq csvprossessor_minio

# Build and start all services
docker-compose up -d --build

# Check logs
docker-compose logs -f api-1
docker-compose logs -f postgres
docker-compose logs -f rabbitmq
```

### Direct Testing
- Swagger UI: `http://localhost:5001/swagger` (api-1) or `http://localhost:5002/swagger` (api-2)
- MinIO Console: `http://localhost:9001` (user: `minioadmin` / pass: `minioadmin`)
- RabbitMQ Management: `http://localhost:15672` (user: `guest` / pass: `guest`)
- PostgreSQL: `psql -h localhost -U postgres -d csvprossessor_db`

### Entity Framework Migrations
```bash
# Add migration (from CSVProssessor.Api or CSVProssessor.Domain directory)
dotnet ef migrations add MigrationName --project CSVProssessor.Infrastructure

# Update database
dotnet ef database update --project CSVProssessor.Infrastructure
```

## RabbitMQ Queue vs Topic Design

- **Queue** (`csv-import-queue`): **Competing consumers** - only one api instance processes each message
  - Use for: Import tasks that shouldn't be processed twice
  - Setup: `BasicQos(prefetchSize: 10)` to load-balance between instances
  
- **Topic** (`csv-changes-topic`): **Fan-out** - all subscribed instances receive the message
  - Use for: Notifications that all instances need to log
  - Setup: Declare durable topic exchange + instance-specific queue binding

## Common Pitfalls & Conventions

1. **Don't skip IoC registration**: All dependencies must go through `IOContainer.SetupIocContainer()`
2. **Use `IUnitOfWork` for transactions**: Never access `DbContext` directly outside repositories
3. **Environment variables trump appsettings.json**: Docker container env vars override config files (by design in `Program.cs`)
4. **RabbitMQ connection pooling**: Both instances share same broker; handle transient failures with exponential backoff
5. **MinIO bucket naming**: Must be lowercase, alphanumeric + hyphens only (Docker env: `MINIO_BUCKET=csvfiles`)

## Code Examples

### Importing CSV (Queue Publisher)
```csharp
// Controller
[HttpPost("csv-import")]
public async Task<IActionResult> ImportCsv(IFormFile file)
{
    var message = new { filename = file.FileName, uploadedAt = DateTime.UtcNow };
    await _rabbitMqService.PublishAsync("csv-import-queue", message);
    return Accepted(new { jobId = Guid.NewGuid() });
}
```

### Processing Queue (BackgroundService)
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var consumer = new EventingBasicConsumer(channel);
    consumer.Received += async (model, ea) =>
    {
        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
        var message = JsonSerializer.Deserialize<CsvImportMessage>(json);
        
        // Download from MinIO
        var csvStream = await _minioService.GetObjectAsync(message.filename);
        
        // Parse & insert into DB
        using var reader = new StreamReader(csvStream);
        // ... parse CSV ...
        await _unitOfWork.SaveAsync();
        
        channel.BasicAck(ea.DeliveryTag, false); // Only ACK after success
    };
    channel.BasicConsume("csv-import-queue", false, consumer);
}
```

## Dependencies & Versions

- **.NET**: 8.0
- **Swashbuckle.AspNetCore**: 6.6.2 (Swagger/OpenAPI)
- **Entity Framework Core**: Integrated (via appsettings)
- **RabbitMQ.Client**: (via NuGet in csproj)
- **Minio .NET SDK**: (via NuGet in csproj)
- **PostgreSQL**: 15-alpine (Docker image)

## Testing Strategy

- Unit tests should mock `IUnitOfWork`, `IRabbitMqService`, `IMinioService`
- Integration tests require `docker-compose up` first
- Health endpoint useful for smoke tests: `GET /api/health`

## Deployment Notes

- **Database migrations** run via `dotnet ef database update` or migrations extension in `MigrationExtensions.cs`
- **Both instances** should use same `docker-compose` - no need for separate containers
- **Scaling**: Add new api-3, api-4 in compose; all will compete on queue automatically
- **Graceful shutdown**: Uses `CancellationToken` in BackgroundService; Docker wait timeout ~30s
