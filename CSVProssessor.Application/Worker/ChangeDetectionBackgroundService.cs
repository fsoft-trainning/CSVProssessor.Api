using CSVProssessor.Application.Interfaces;
using CSVProssessor.Domain.DTOs.CsvJobDTOs;
using CSVProssessor.Infrastructure.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;

namespace CSVProssessor.Application.Worker
{
    /// <summary>
    /// Background service that runs periodically (every 5 minutes) to detect changes in CSV records
    /// and publish notifications to RabbitMQ topic "csv-changes-topic".
    /// All instances (api-1, api-2) subscribe to this topic and receive notifications (fan-out pattern).
    /// </summary>
    public class ChangeDetectionBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private const int CheckIntervalMinutes = 1;

        public ChangeDetectionBackgroundService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var loggerService = scope.ServiceProvider.GetRequiredService<ILoggerService>();

            loggerService.Info("ChangeDetectionBackgroundService started");

            // Get instance name from environment variable
            var instanceName = Environment.GetEnvironmentVariable("INSTANCE_NAME") ?? "api-unknown";

            try
            {
                // Initial delay to allow all services to start
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

                // Keep running and check periodically
                while (!stoppingToken.IsCancellationRequested)
                {
                    await PerformChangeDetectionCheckAsync(instanceName, stoppingToken);
                    await Task.Delay(TimeSpan.FromMinutes(CheckIntervalMinutes), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                using var cancelScope = _serviceProvider.CreateScope();
                var cancelLogger = cancelScope.ServiceProvider.GetRequiredService<ILoggerService>();
                cancelLogger.Info("ChangeDetectionBackgroundService is shutting down gracefully");
            }
            catch (Exception ex)
            {
                using var fatalScope = _serviceProvider.CreateScope();
                var fatalLogger = fatalScope.ServiceProvider.GetRequiredService<ILoggerService>();
                fatalLogger.Error($"Fatal error in ChangeDetectionBackgroundService: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Performs a single change detection check cycle
        /// </summary>
        private async Task PerformChangeDetectionCheckAsync(string instanceName, CancellationToken stoppingToken)
        {
            try
            {
                // Create a new scope for each check to resolve scoped services
                using var checkScope = _serviceProvider.CreateScope();
                var csvService = checkScope.ServiceProvider.GetRequiredService<ICsvService>();
                var scopedLogger = checkScope.ServiceProvider.GetRequiredService<ILoggerService>();

                scopedLogger.Info($"[{instanceName}] Starting change detection check...");

                // Create request DTO
                var request = new DetectChangesRequestDto
                {
                    LastCheckTime = null, // Will use minutesBack
                    MinutesBack = CheckIntervalMinutes,
                    ChangeType = "Created",
                    InstanceName = instanceName,
                    PublishToTopic = true
                };

                // Call the detect and publish method
                var response = await csvService.DetectAndPublishChangesAsync(request);

                // Log and persist results
                await HandleDetectionResultsAsync(response, instanceName, scopedLogger, stoppingToken);
            }
            catch (Exception ex)
            {
                await HandleDetectionErrorAsync(instanceName, ex, stoppingToken);
            }

            // Wait for the next check interval
            using var waitScope = _serviceProvider.CreateScope();
            var waitLogger = waitScope.ServiceProvider.GetRequiredService<ILoggerService>();
            waitLogger.Info($"[{instanceName}] Waiting {CheckIntervalMinutes} minutes until next check...");
        }

        /// <summary>
        /// Handles detection results: logs success/no-changes and writes to file
        /// </summary>
        private async Task HandleDetectionResultsAsync(
            DetectChangesResponseDto response,
            string instanceName,
            ILoggerService logger,
            CancellationToken cancellationToken)
        {
            if (response.TotalChanges > 0)
            {
                var message = $"[{instanceName}] {response.Message} - Changes: {response.TotalChanges}, Published: {response.PublishedToTopic}";
                logger.Success(message);
                await LogToFileAsync(message, cancellationToken);
            }
            else
            {
                var message = $"[{instanceName}] {response.Message}";
                logger.Info(message);
                await LogToFileAsync(message, cancellationToken);
            }
        }

        /// <summary>
        /// Handles detection errors: logs and writes error to file
        /// </summary>
        private async Task HandleDetectionErrorAsync(
            string instanceName,
            Exception ex,
            CancellationToken cancellationToken)
        {
            using var errorScope = _serviceProvider.CreateScope();
            var errorLogger = errorScope.ServiceProvider.GetRequiredService<ILoggerService>();
            errorLogger.Error($"[{instanceName}] Error during change detection: {ex.Message}\n{ex.StackTrace}");
            
            // Write error to file log
            await LogToFileAsync($"[ERROR] [{instanceName}] {ex.Message}", cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var loggerService = scope.ServiceProvider.GetRequiredService<ILoggerService>();
            loggerService.Info("ChangeDetectionBackgroundService stopped");
            await base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// Logs change detection messages to a file in Materials/Logs directory
        /// File name: csv-changes-log.txt
        /// </summary>
        private async Task LogToFileAsync(string message, CancellationToken cancellationToken)
        {
            try
            {
                // Get current working directory (works in both Windows and Docker)
                var baseDir = Directory.GetCurrentDirectory();
                var logsDir = Path.Combine(baseDir, "Materials", "Logs");
                var logFilePath = Path.Combine(logsDir, "csv-changes-log.txt");

                // Ensure directory exists
                Directory.CreateDirectory(logsDir);

                // Format log message with timestamp
                var timestampedMessage = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}";

                // Write to file
                using (var fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(fileStream))
                {
                    await writer.WriteLineAsync(timestampedMessage);
                    await writer.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                // Silently fail to avoid breaking the service
                System.Diagnostics.Debug.WriteLine($"Failed to write log file: {ex.Message}");
            }
        }
    }
}
