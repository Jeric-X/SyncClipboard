using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.RemoteServer.Adapter;

public sealed record StorageProfileSnapshot(ProfileDto Profile, string? Version);

public interface IStorageBasedServerAdapter : IServerAdapter
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<ProfileDto?> GetProfileAsync(CancellationToken cancellationToken = default);

    Task<StorageProfileSnapshot?> GetProfileSnapshotAsync(CancellationToken cancellationToken = default);

    Task SetProfileAsync(ProfileDto profileDto, CancellationToken cancellationToken = default);

    Task<bool> TrySetProfileAsync(ProfileDto profileDto, string? expectedVersion, CancellationToken cancellationToken = default);

    Task UploadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    Task DownloadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    Task CleanupTempFilesAsync(CancellationToken cancellationToken = default);
}
