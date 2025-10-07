using SyncClipboard.Abstract;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.RemoteServer.Adapter;

public interface IStorageBasedServerAdapter : IServerAdapter
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<ClipboardProfileDTO?> GetProfileAsync(CancellationToken cancellationToken = default);

    Task SetProfileAsync(ClipboardProfileDTO profileDto, CancellationToken cancellationToken = default);

    Task UploadFileAsync(string fileName, string localPath, CancellationToken cancellationToken = default);

    Task DownloadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    Task CleanupTempFilesAsync(CancellationToken cancellationToken = default);
}