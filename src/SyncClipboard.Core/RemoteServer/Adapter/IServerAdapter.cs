using System.Net;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.RemoteServer.Adapter;

public interface IServerAdapter
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<ProfileDto?> GetProfileAsync(CancellationToken cancellationToken = default);

    Task SetProfileAsync(ProfileDto profileDto, CancellationToken cancellationToken = default);

    Task UploadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    Task DownloadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    Task CleanupTempFilesAsync(CancellationToken cancellationToken = default);

    Task TestConnectionAsync(CancellationToken cancellationToken = default);
    void SetConfig(object config, SyncConfig syncConfig);
    void ApplyConfig();
    void SetProxy(IWebProxy proxy);
}

public interface IServerAdapter<T> : IServerAdapter where T : IAdapterConfig<T>
{
    void SetConfig(T config, SyncConfig syncConfig);

    void IServerAdapter.SetConfig(object config, SyncConfig syncConfig)
    {
        SetConfig((T)config, syncConfig);
    }
}
