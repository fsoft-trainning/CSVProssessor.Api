using CSVProssessor.Application.Interfaces;
using CSVProssessor.Application.Interfaces.Common;
using CSVProssessor.Application.Utils;
using CSVProssessor.Domain.DTOs.CsvJobDTOs;
using CSVProssessor.Domain.Entities;
using CSVProssessor.Domain.Enums;
using CSVProssessor.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Http;
using System.IO.Compression;
using System.Text.Json;

namespace CSVProssessor.Application.Services
{
    public class CsvService : ICsvService
    {
        public readonly IUnitOfWork _unitOfWork;
        public readonly IBlobService _blobService;
        public readonly IRabbitMqService _rabbitMqService;

        public CsvService(IUnitOfWork unitOfWork, IBlobService blobService, IRabbitMqService rabbitMqService)
        {
            _unitOfWork = unitOfWork;
            _blobService = blobService;
            _rabbitMqService = rabbitMqService;
        }

        public async Task<ImportCsvResponseDto> ImportCsvAsync(IFormFile file)
        {
            // Kiểm tra input hợp lệ
            if (file == null)
                throw ErrorHelper.BadRequest("File không được để trống.");

            if (file.Length == 0)
                throw ErrorHelper.BadRequest("File không được rỗng.");

            if (string.IsNullOrWhiteSpace(file.FileName))
                throw ErrorHelper.BadRequest("Tên file không được để trống.");

            // Generate unique file name to avoid conflicts
            var originalFileName = file.FileName;
            var fileExtension = Path.GetExtension(originalFileName);
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8); // Short GUID (8 chars)
            var uniqueFileName = $"{fileNameWithoutExtension}_{timestamp}_{uniqueId}{fileExtension}";

            // Đọc file stream từ IFormFile
            using var stream = file.OpenReadStream();

            // 1. tải file lên MinIO blob storage với unique name
            await _blobService.UploadFileAsync(uniqueFileName, stream);

            // 2. ghi nhận job vào database
            var jobId = Guid.NewGuid();
            var csvJob = new CsvJob
            {
                Id = jobId,
                FileName = uniqueFileName,
                OriginalFileName = originalFileName,
                Type = CsvJobType.Import,
                Status = CsvJobStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _unitOfWork.CsvJobs.AddAsync(csvJob);
            await _unitOfWork.SaveChangesAsync();

            // 3. Prepare and publish message to RabbitMQ queue
            var message = new CsvImportMessage
            {
                JobId = jobId,
                FileName = uniqueFileName,
                UploadedAt = DateTime.UtcNow
            };

            // Publish message vào queue cho background service xử lý
            await _rabbitMqService.PublishAsync("csv-import-queue", message);

            // Return response DTO
            return new ImportCsvResponseDto
            {
                JobId = jobId,
                FileName = uniqueFileName,
                UploadedAt = DateTime.UtcNow,
                Status = csvJob.Status.ToString(),
                Message = $"File CSV đã được tải lên thành công với tên: {uniqueFileName}. Background service sẽ xử lý trong thời gian sớm nhất."
            };
        }

        /// <summary>
        /// Process CSV import: download file from MinIO, parse it, and save records to database
        /// Called by CsvImportQueueListenerService
        /// Handles: Download → Parse CSV → Save to DB → Update job status
        /// </summary>
        public async Task SaveCsvRecordsAsync(Guid jobId, string fileName)
        {
            // 1. Download file from MinIO
            using var fileStream = await _blobService.DownloadFileAsync(fileName);

            // 2. Parse CSV file
            var records = await ParseCsvAsync(jobId, fileName, fileStream);

            if (records == null || records.Count == 0)
                throw ErrorHelper.BadRequest("Không có records để lưu vào database.");

            // 3. Add all records to database
            foreach (var record in records)
            {
                await _unitOfWork.CsvRecords.AddAsync(record);
            }

            // 4. Save changes to database
            await _unitOfWork.SaveChangesAsync();

            // 5. Update job status to Completed
            var csvJob = await _unitOfWork.CsvJobs.FirstOrDefaultAsync(x => x.Id == jobId);
            if (csvJob != null)
            {
                csvJob.Status = CsvJobStatus.Completed;
                csvJob.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Parse CSV file into CsvRecord list
        /// </summary>
        private async Task<List<CsvRecord>> ParseCsvAsync(Guid jobId, string fileName, Stream fileStream)
        {
            var records = new List<CsvRecord>();
            using var reader = new StreamReader(fileStream);

            // Đọc header
            var headerLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(headerLine))
                return records;

            var headers = headerLine.Split(',').Select(h => h.Trim()).ToArray();

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var values = line.Split(',').Select(v => v.Trim()).ToArray();

                // Create JSON object as string and parse it
                var jsonDict = new Dictionary<string, object>();
                for (int i = 0; i < headers.Length && i < values.Length; i++)
                {
                    jsonDict[headers[i]] = values[i];
                }

                var jsonString = JsonSerializer.Serialize(jsonDict);
                var jsonDoc = JsonDocument.Parse(jsonString);

                records.Add(new CsvRecord
                {
                    JobId = jobId,
                    FileName = fileName,
                    ImportedAt = DateTime.UtcNow,
                    Data = jsonDoc
                });
            }

            return records;
        }


        /// <summary>
        /// Detect changes and publish notification to RabbitMQ topic "csv-changes-topic"
        /// Called by ChangeDetectionBackgroundService to check for changes and notify all instances.
        /// </summary>
        /// <param name="request">Request containing detection parameters</param>
        /// <returns>Response containing detected changes and publishing status</returns>
        public async Task<DetectChangesResponseDto> DetectAndPublishChangesAsync(DetectChangesRequestDto request)
        {
            DateTime timeThreshold;

            if (request.LastCheckTime.HasValue)
            {
                // Use provided lastCheckTime
                timeThreshold = request.LastCheckTime.Value;
            }
            else
            {
                // Calculate time threshold based on minutesBack
                timeThreshold = DateTime.UtcNow.AddMinutes(-request.MinutesBack);
            }

            // Query records created or updated since time threshold
            var records = await _unitOfWork.CsvRecords.GetAllAsync(x =>
                (x.CreatedAt >= timeThreshold || (x.UpdatedAt.HasValue && x.UpdatedAt >= timeThreshold))
                && !x.IsDeleted
            );

            bool publishedToTopic = false;

            // Publish notification if changes detected and publishToTopic is true
            if (request.PublishToTopic && records.Count > 0)
            {
                var notificationMessage = new CsvChangeNotificationMessage
                {
                    ChangeType = request.ChangeType,
                    RecordIds = records.Select(x => x.Id).ToList(),
                    TotalChanges = records.Count,
                    DetectedAt = DateTime.UtcNow,
                    CheckStartTime = timeThreshold,
                    CheckEndTime = DateTime.UtcNow,
                    InstanceName = request.InstanceName
                };

                // Publish to topic - all subscribed instances will receive this message
                await _rabbitMqService.PublishToTopicAsync("csv-changes-topic", notificationMessage);
                publishedToTopic = true;
            }

            // Map records to DTO
            var recordDtos = records.Select(x => new CsvRecordDto
            {
                Id = x.Id,
                JobId = x.JobId,
                FileName = x.FileName,
                ImportedAt = x.ImportedAt,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            }).ToList();

            // Build response
            var response = new DetectChangesResponseDto
            {
                Changes = recordDtos,
                TotalChanges = records.Count,
                PublishedToTopic = publishedToTopic,
                DetectedAt = DateTime.UtcNow,
                CheckStartTime = timeThreshold,
                CheckEndTime = DateTime.UtcNow,
                Message = records.Count > 0
                    ? $"Detected {records.Count} changes. Published: {publishedToTopic}"
                    : "No changes detected"
            };

            return response;
        }


        public async Task<ListCsvFilesResponseDto> ListAllCsvFilesAsync()
        {
            // 1. Query all CSV import jobs from database
            var csvJobs = await _unitOfWork.CsvJobs.GetAllAsync(x =>
                x.Type == CsvJobType.Import && !x.IsDeleted
            );

            if (csvJobs == null || csvJobs.Count == 0)
            {
                return new ListCsvFilesResponseDto
                {
                    TotalFiles = 0,
                    Files = new List<CsvFileInfoDto>(),
                    GeneratedAt = DateTime.UtcNow,
                    Message = "Không có file CSV nào trong hệ thống."
                };
            }

            // 2. Group by filename and get metadata for each file
            var fileInfoList = new List<CsvFileInfoDto>();

            foreach (var job in csvJobs)
            {
                // Count records for this job
                var recordCount = await _unitOfWork.CsvRecords.CountAsync(x =>
                    x.JobId == job.Id && !x.IsDeleted
                );

                fileInfoList.Add(new CsvFileInfoDto
                {
                    FileName = job.FileName,
                    OriginalFileName = job.OriginalFileName,
                    JobId = job.Id,
                    UploadedAt = job.CreatedAt,
                    Status = job.Status.ToString(),
                    RecordCount = recordCount
                });
            }

            // 3. Build response
            var response = new ListCsvFilesResponseDto
            {
                TotalFiles = fileInfoList.Count,
                Files = fileInfoList.OrderByDescending(x => x.UploadedAt).ToList(),
                GeneratedAt = DateTime.UtcNow,
                Message = $"Tìm thấy {fileInfoList.Count} file CSV trong hệ thống."
            };

            return response;
        }


        public async Task<Stream> ExportSingleCsvFileAsync(string fileName)
        {
            // 1. Validate input
            if (string.IsNullOrWhiteSpace(fileName))
                throw ErrorHelper.BadRequest("Tên file không được để trống.");

            // 2. Check if file exists in database
            var csvJob = await _unitOfWork.CsvJobs.FirstOrDefaultAsync(x =>
                x.FileName == fileName && x.Type == CsvJobType.Import && !x.IsDeleted
            );

            if (csvJob == null)
                throw ErrorHelper.NotFound($"Không tìm thấy file '{fileName}' trong hệ thống.");

            // 3. Download file from MinIO
            try
            {
                var fileStream = await _blobService.DownloadFileAsync(fileName);
                return fileStream;
            }
            catch (Exception ex)
            {
                throw ErrorHelper.Internal($"Lỗi khi download file '{fileName}': {ex.Message}");
            }
        }


        public async Task<Stream> ExportAllCsvFilesAsync()
        {
            var csvJobs = await _unitOfWork.CsvJobs.GetAllAsync(x =>
                x.Type == CsvJobType.Import && !x.IsDeleted);

            if (csvJobs == null || csvJobs.Count == 0)
                throw ErrorHelper.BadRequest("Không có file CSV nào để export.");

            var uniqueFileNames = csvJobs
                .Select(x => x.FileName)
                .Distinct()
                .ToList();

            var zipStream = new MemoryStream();

            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                foreach (var fileName in uniqueFileNames)
                {
                    await AddFileToZipArchiveAsync(archive, fileName);
                }
            }

            zipStream.Position = 0;
            return zipStream;
        }

        private async Task AddFileToZipArchiveAsync(ZipArchive archive, string fileName)
        {
            try
            {
                using var fileStream = await _blobService.DownloadFileAsync(fileName);
                var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);

                using var entryStream = entry.Open();
                await fileStream.CopyToAsync(entryStream);
            }
            catch (Exception ex)
            {
                throw ErrorHelper.Internal($"Lỗi khi download file '{fileName}': {ex.Message}");
            }
        }
    }
}