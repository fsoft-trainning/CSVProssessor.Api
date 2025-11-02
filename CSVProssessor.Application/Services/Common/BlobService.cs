using CSVProssessor.Application.Interfaces.Common;
using CSVProssessor.Application.Utils;
using CSVProssessor.Infrastructure.Interfaces;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace CSVProssessor.Application.Services.Common
{
    public class BlobService : IBlobService
    {
        private readonly string _bucketName = "csvfiles";
        private readonly ILoggerService _logger;
        private readonly IMinioClient _minioClient;

        public BlobService(ILoggerService logger)
        {
            _logger = logger;

            // Cần cấu hình các biến môi trường sau:
            // - MINIO_ENDPOINT
            // - MINIO_ACCESS_KEY
            // - MINIO_SECRET_KEY
            var endpoint = Environment.GetEnvironmentVariable("MINIO_ENDPOINT")?.Trim();
            var accessKey = Environment.GetEnvironmentVariable("MINIO_ACCESS_KEY")?.Trim();
            var secretKey = Environment.GetEnvironmentVariable("MINIO_SECRET_KEY")?.Trim();

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey) ||
                string.IsNullOrWhiteSpace(secretKey))
            {
                throw new InvalidOperationException(
                    "MinIO configuration is missing. Please set MINIO_ENDPOINT, MINIO_ACCESS_KEY, and MINIO_SECRET_KEY environment variables.");
            }


            // Remove http:// or https:// prefix if present - MinIO expects just host:port
            var cleanEndpoint = endpoint
                .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                .Trim();


            // Kết nối MinIO không dùng SSL (vì đang dùng IP:port hoặc HTTP)
            _minioClient = new MinioClient()
                .WithEndpoint(cleanEndpoint)
                .WithCredentials(accessKey, secretKey)
                .WithSSL(false)
                .Build();
        }

        public async Task UploadFileAsync(string fileName, Stream fileStream)
        {
            try
            {
                // Kiểm tra bucket tồn tại, nếu chưa thì tạo mới
                var beArgs = new BucketExistsArgs().WithBucket(_bucketName);
                var found = await _minioClient.BucketExistsAsync(beArgs);

                if (!found)
                {
                    var mbArgs = new MakeBucketArgs().WithBucket(_bucketName);
                    await _minioClient.MakeBucketAsync(mbArgs);
                }

                // Lấy content type dựa trên phần mở rộng của file
                var contentType = GetContentType(fileName);

                var putObjectArgs = new PutObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(fileName)
                    .WithStreamData(fileStream)
                    .WithObjectSize(fileStream.Length)
                    .WithContentType(contentType);

                await _minioClient.PutObjectAsync(putObjectArgs);
            }
            catch (MinioException minioEx)
            {
                _logger.Error($"MinIO Error during upload: {minioEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error during file upload: {ex.Message}");
                throw;
            }
        }

        public async Task<string> GetPreviewUrlAsync(string fileName)
        {
            // Biến MINIO_HOST phải trỏ tới reverse proxy HTTPS, vd: https://minio.fpt-devteam.fun
            var minioHost = Environment.GetEnvironmentVariable("MINIO_HOST") ?? "https://minio.fpt-devteam.fun";

            // Sử dụng Base64 encoding thay vì URL encoding để phù hợp với định dạng API
            var base64File = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(fileName));

            // URL được định dạng đúng với API reverse proxy
            var previewUrl =
                $"{minioHost}/api/v1/buckets/{_bucketName}/objects/download?preview=true&prefix={base64File}&version_id=null";
            _logger.Info($"Preview URL generated: {previewUrl}");

            return previewUrl;
        }

        public async Task<string> GetFileUrlAsync(string fileName)
        {
            try
            {
                var args = new PresignedGetObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(fileName)
                    .WithExpiry(7 * 24 * 60 * 60);

                var fileUrl = await _minioClient.PresignedGetObjectAsync(args);

                // Replace internal MinIO URL with public URL for external access
                var minioPublicUrl = Environment.GetEnvironmentVariable("MINIO_PUBLIC_URL");
                if (!string.IsNullOrWhiteSpace(minioPublicUrl))
                {
                    // Replace minio:9000 with localhost:9000 (or configured public URL)
                    fileUrl = fileUrl.Replace("minio:9000",
                        minioPublicUrl.Replace("http://", "").Replace("https://", ""));
                }

                _logger.Success($"Presigned file URL generated: {fileUrl}");
                return fileUrl;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error generating file URL: {ex.Message}");
                throw;
            }
        }

        public async Task<Stream> DownloadFileAsync(string fileName)
        {
            _logger.Info($"Downloading file: {fileName}");

            try
            {
                var memoryStream = new MemoryStream();
                var getObjectArgs = new GetObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(fileName)
                    .WithCallbackStream(async (stream) => { await stream.CopyToAsync(memoryStream); });

                await _minioClient.GetObjectAsync(getObjectArgs);
                memoryStream.Position = 0; // Reset stream position to beginning
                _logger.Success($"File '{fileName}' downloaded successfully. Size: {memoryStream.Length} bytes");
                return memoryStream;
            }
            catch (MinioException minioEx)
            {
                _logger.Error($"MinIO Error during download: {minioEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error during file download: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteFileAsync(string fileName)
        {
            _logger.Info($"Deleting file: {fileName}");

            try
            {
                var removeObjectArgs = new RemoveObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(fileName);

                await _minioClient.RemoveObjectAsync(removeObjectArgs);
                _logger.Success($"File '{fileName}' deleted successfully.");
            }
            catch (MinioException minioEx)
            {
                _logger.Error($"MinIO Error during delete: {minioEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error during file delete: {ex.Message}");
                throw;
            }
        }

        public async Task<string> ReplaceImageAsync(Stream newImageStream, string originalFileName, string? oldImageUrl,
            string containerPrefix)
        {
            try
            {
                // Xóa ảnh cũ nếu có
                if (!string.IsNullOrWhiteSpace(oldImageUrl))
                    try
                    {
                        var oldFileName = Path.GetFileName(new Uri(oldImageUrl).LocalPath);
                        var fullOldPath = $"{containerPrefix}/{oldFileName}";
                        await DeleteFileAsync(fullOldPath);
                        _logger.Info($"[ReplaceImageAsync] Deleted old image: {fullOldPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"[ReplaceImageAsync] Failed to delete old image: {ex.Message}");
                    }

                // Upload ảnh mới
                var newFileName = $"{containerPrefix}/{Guid.NewGuid()}{Path.GetExtension(originalFileName)}";
                _logger.Info($"[ReplaceImageAsync] Uploading new image: {newFileName}");

                await UploadFileAsync(newFileName, newImageStream);

                var previewUrl = await GetPreviewUrlAsync(newFileName);
                _logger.Success($"[ReplaceImageAsync] Uploaded and generated preview URL: {previewUrl}");
                return previewUrl;
            }
            catch (Exception ex)
            {
                _logger.Error($"[ReplaceImageAsync] Error occurred: {ex.Message}");
                throw ErrorHelper.Internal("Lỗi khi xử lý ảnh.");
            }
        }

        private string GetContentType(string fileName)
        {
            _logger.Info($"Determining content type for file: {fileName}");
            var extension = Path.GetExtension(fileName)?.ToLower();

            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".pdf" => "application/pdf",
                ".mp4" => "video/mp4",
                _ => "application/octet-stream" // fallback nếu định dạng không rõ
            };
        }
    }
}