using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.RemoteServer.Adapter;

public sealed record StorageProfileSnapshot(ProfileDto Profile, string? Version);

public interface IStorageBasedServerAdapter : IServerAdapter
{
    Task<StorageProfileSnapshot?> GetProfileSnapshotAsync(CancellationToken cancellationToken = default);

    Task<bool> TrySetProfileAsync(ProfileDto profileDto, string? expectedVersion, CancellationToken cancellationToken = default);
}
