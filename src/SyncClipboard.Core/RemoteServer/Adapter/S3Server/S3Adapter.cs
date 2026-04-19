using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using System.Net;
using System.Text.Json;

namespace SyncClipboard.Core.RemoteServer.Adapter.S3Server;

public sealed class S3Adapter : IServerAdapter<S3Config>, IStorageBasedServerAdapter, IDisposable
{
    private const string RemoteProfilePath = "SyncClipboard.json";
    private const string RemoteFileFolder = "file";
    private const int BufferSize = 1024 * 128;

    private readonly ILogger _logger;
    private readonly object _clientLock = new();

    private S3Config _s3Config = new();
    private SyncConfig _syncConfig = new();
    private AmazonS3Client _s3Client;
    private bool IsCustomEndpoint => !string.IsNullOrWhiteSpace(_s3Config.ServiceURL);

    public S3Adapter(ILogger logger)
    {
        _logger = logger;
        _s3Client = CreateClient();
    }

    public void SetConfig(S3Config config, SyncConfig syncConfig)
    {
        _s3Config = config;
        _syncConfig = syncConfig;
    }

    public void ApplyConfig()
    {
        lock (_clientLock)
        {
            _s3Client.Dispose();
            _s3Client = CreateClient();
        }
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfig();
        var request = new ListObjectsV2Request
        {
            BucketName = _s3Config.BucketName,
            Prefix = BuildObjectKey(string.Empty),
            MaxKeys = 1
        };
        await _s3Client.ListObjectsV2Async(request, cancellationToken);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfig();
        var fileFolderMarker = $"{BuildObjectKey(RemoteFileFolder)}/";

        try
        {
            var metadataRequest = new GetObjectMetadataRequest
            {
                BucketName = _s3Config.BucketName,
                Key = fileFolderMarker
            };
            await _s3Client.GetObjectMetadataAsync(metadataRequest, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = _s3Config.BucketName,
                Key = fileFolderMarker,
                ContentBody = string.Empty,
                ContentType = "application/x-directory"
            };
            await _s3Client.PutObjectAsync(putRequest, cancellationToken);
        }
    }

    public async Task<ProfileDto?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfig();
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _s3Config.BucketName,
                Key = BuildObjectKey(RemoteProfilePath)
            };
            using var response = await _s3Client.GetObjectAsync(request, cancellationToken);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync(cancellationToken);
            return JsonSerializer.Deserialize<ProfileDto>(json, JsonSerializerOptions.Web);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
        {
            return null;
        }
    }

    public async Task SetProfileAsync(ProfileDto profileDto, CancellationToken cancellationToken = default)
    {
        ValidateConfig();
        var json = JsonSerializer.Serialize(profileDto, JsonSerializerOptions.Web);
        var request = new PutObjectRequest
        {
            BucketName = _s3Config.BucketName,
            Key = BuildObjectKey(RemoteProfilePath),
            ContentBody = json,
            ContentType = "application/json; charset=utf-8"
        };
        ApplyCompatibilityForPut(request);
        await _s3Client.PutObjectAsync(request, cancellationToken);
    }

    public async Task UploadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateConfig();
        var key = BuildObjectKey(BuildFileObjectPath(fileName));
        await using var localStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var progressStream = new ProgressReadStream(localStream, progress);

        var request = new PutObjectRequest
        {
            BucketName = _s3Config.BucketName,
            Key = key,
            InputStream = progressStream,
            AutoCloseStream = false
        };
        ApplyCompatibilityForPut(request);
        await _s3Client.PutObjectAsync(request, cancellationToken);
        progress?.Report(new HttpDownloadProgress
        {
            BytesReceived = (ulong)localStream.Length,
            TotalBytesToReceive = (ulong)localStream.Length,
            End = true
        });
        _logger.Write($"[S3] Upload completed for {fileName}");
    }

    public async Task DownloadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateConfig();
        var key = BuildObjectKey(BuildFileObjectPath(fileName));
        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var request = new GetObjectRequest
        {
            BucketName = _s3Config.BucketName,
            Key = key
        };

        using var response = await _s3Client.GetObjectAsync(request, cancellationToken);
        var totalBytes = response.ContentLength > 0 ? (ulong?)response.ContentLength : null;
        await using var responseStream = response.ResponseStream;
        await using var outputStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[BufferSize];
        ulong copied = 0;
        while (true)
        {
            var bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead <= 0)
            {
                break;
            }

            await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            copied += (ulong)bytesRead;

            progress?.Report(new HttpDownloadProgress
            {
                BytesReceived = copied,
                TotalBytesToReceive = totalBytes,
                End = false
            });
        }

        progress?.Report(new HttpDownloadProgress
        {
            BytesReceived = copied,
            TotalBytesToReceive = totalBytes,
            End = true
        });
        _logger.Write($"[S3] Downloaded {fileName} to {localPath}");
    }

    public async Task CleanupTempFilesAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfig();
        if (!_s3Config.DeletePreviousFilesOnPush)
        {
            return;
        }

        var prefix = $"{BuildObjectKey(RemoteFileFolder)}/";
        string? continuationToken = null;

        do
        {
            var listRequest = new ListObjectsV2Request
            {
                BucketName = _s3Config.BucketName,
                Prefix = prefix,
                ContinuationToken = continuationToken,
                MaxKeys = 1000
            };
            var listResponse = await _s3Client.ListObjectsV2Async(listRequest, cancellationToken);
            continuationToken = listResponse.IsTruncated ? listResponse.NextContinuationToken : null;

            if (listResponse.S3Objects.Count == 0)
            {
                continue;
            }

            var deleteRequest = new DeleteObjectsRequest
            {
                BucketName = _s3Config.BucketName
            };
            foreach (var obj in listResponse.S3Objects)
            {
                deleteRequest.AddKey(obj.Key);
            }

            try
            {
                await _s3Client.DeleteObjectsAsync(deleteRequest, cancellationToken);
            }
            catch (AmazonS3Exception ex)
            {
                if (ex.Message != "Missing required header for this request: Content-MD5")
                {
                    throw;
                }

                foreach (var obj in listResponse.S3Objects)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var deleteObj = new DeleteObjectRequest
                    {
                        BucketName = _s3Config.BucketName,
                        Key = obj.Key
                    };
                    await _s3Client.DeleteObjectAsync(deleteObj, cancellationToken);
                }
            }
        } while (!string.IsNullOrEmpty(continuationToken));
    }

    public void Dispose()
    {
        _s3Client.Dispose();
    }

    private AmazonS3Client CreateClient()
    {
        var credentials = new BasicAWSCredentials(_s3Config.AccessKeyId, _s3Config.SecretAccessKey);
        var config = new AmazonS3Config();
        var serviceUrl = _s3Config.ServiceURL.Trim();
        var timeoutSeconds = _syncConfig.TimeOut == 0 ? 100u : _syncConfig.TimeOut;
        config.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
        config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;

        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            config.ServiceURL = serviceUrl.TrimEnd('/');
            config.ForcePathStyle = _s3Config.ForcePathStyle;
            config.UseHttp = serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(_s3Config.Region))
            {
                config.AuthenticationRegion = _s3Config.Region.Trim();
            }
        }
        else if (!string.IsNullOrWhiteSpace(_s3Config.Region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(_s3Config.Region.Trim());
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.USEast1;
        }

        return new AmazonS3Client(credentials, config);
    }

    private void ApplyCompatibilityForPut(PutObjectRequest request)
    {
        if (!IsCustomEndpoint)
        {
            return;
        }

        // Many S3-compatible endpoints (e.g. R2/OSS gateways) do not implement
        // streaming trailer signatures used by newer AWS SDK defaults.
        request.UseChunkEncoding = false;
        request.DisablePayloadSigning = true;
    }

    private void ValidateConfig()
    {
        if (string.IsNullOrWhiteSpace(_s3Config.BucketName))
        {
            throw new ArgumentException("BucketName cannot be empty.");
        }
        if (string.IsNullOrWhiteSpace(_s3Config.AccessKeyId))
        {
            throw new ArgumentException("AccessKeyId cannot be empty.");
        }
        if (string.IsNullOrWhiteSpace(_s3Config.SecretAccessKey))
        {
            throw new ArgumentException("SecretAccessKey cannot be empty.");
        }
    }

    private string BuildObjectKey(string relativePath)
    {
        var normalizedRelativePath = relativePath.Replace('\\', '/').Trim('/');
        var normalizedPrefix = _s3Config.ObjectPrefix.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(normalizedPrefix))
        {
            return normalizedRelativePath;
        }
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return normalizedPrefix;
        }
        return $"{normalizedPrefix}/{normalizedRelativePath}";
    }

    private static string BuildFileObjectPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("fileName cannot be null or empty", nameof(fileName));
        }
        return $"{RemoteFileFolder}/{fileName}";
    }

    private sealed class ProgressReadStream(Stream inner, IProgress<HttpDownloadProgress>? progress) : Stream
    {
        private readonly Stream _inner = inner;
        private readonly IProgress<HttpDownloadProgress>? _progress = progress;
        private readonly ulong? _totalBytes = inner.CanSeek ? (ulong)inner.Length : null;
        private ulong _readBytes;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = _inner.Read(buffer, offset, count);
            ReportProgress(bytesRead);
            return bytesRead;
        }

        public override int Read(Span<byte> buffer)
        {
            var bytesRead = _inner.Read(buffer);
            ReportProgress(bytesRead);
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytesRead = await _inner.ReadAsync(buffer, cancellationToken);
            ReportProgress(bytesRead);
            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var bytesRead = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            ReportProgress(bytesRead);
            return bytesRead;
        }

        private void ReportProgress(int bytesRead)
        {
            if (_progress is null)
            {
                return;
            }

            if (bytesRead > 0)
            {
                _readBytes += (ulong)bytesRead;
                _progress.Report(new HttpDownloadProgress
                {
                    BytesReceived = _readBytes,
                    TotalBytesToReceive = _totalBytes,
                    End = false
                });
            }
            else
            {
                _progress.Report(new HttpDownloadProgress
                {
                    BytesReceived = _readBytes,
                    TotalBytesToReceive = _totalBytes,
                    End = true
                });
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new NotSupportedException();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
