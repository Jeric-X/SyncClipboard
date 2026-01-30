using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.RemoteServer.Adapter.Default;

/// <summary>
/// 默认的存储适配器实现，所有方法都抛出异常
/// 用于在没有可用适配器时提供一个安全的默认行为
/// </summary>
public sealed class DefaultStorageAdapter : IStorageBasedServerAdapter
{
    private const string ErrorMessage = "No valid server account configured.";

    public void SetConfig(object config, SyncConfig syncConfig)
    {
    }

    public void ApplyConfig()
    {
    }

    public Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(ErrorMessage);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(ErrorMessage);
    }

    public Task<ProfileDto?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(ErrorMessage);
    }

    public Task SetProfileAsync(ProfileDto profileDto, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(ErrorMessage);
    }

    public Task UploadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(ErrorMessage);
    }

    public Task DownloadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(ErrorMessage);
    }

    public Task CleanupTempFilesAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(ErrorMessage);
    }
}