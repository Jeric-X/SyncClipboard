using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Interfaces;

public interface IRemoteClipboardServer : IDisposable
{
    Task<Profile> GetProfileAsync(CancellationToken cancellationToken = default);
    Task SetProfileAsync(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
    Task DownloadProfileDataAsync(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    event EventHandler<ProfileChangedEventArgs> RemoteProfileChanged;
    event EventHandler<PollStatusEventArgs> PollStatusEvent;
    void OnSyncConfigChanged(SyncConfig syncConfig);
}