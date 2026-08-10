using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.RemoteServer;

public sealed class EmptyRemoteClipboardServer : IRemoteClipboardServer
{
    public static EmptyRemoteClipboardServer Instance { get; } = new();

    private EmptyRemoteClipboardServer()
    {
    }

    public event EventHandler<ProfileChangedEventArgs> RemoteProfileChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<PollStatusEventArgs> PollStatusEvent
    {
        add { }
        remove { }
    }

    public Task<Profile> GetProfileAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<Profile>(CreateUnavailableException());

    public Task SetProfileAsync(
        Profile profile,
        IProgress<HttpDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException(CreateUnavailableException());

    public Task DownloadProfileDataAsync(
        Profile profile,
        IProgress<HttpDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException(CreateUnavailableException());

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public void OnSyncConfigChanged(SyncConfig syncConfig)
    {
    }

    public void Dispose()
    {
    }

    private static InvalidOperationException CreateUnavailableException() => new(Strings.SyncAccountNotSelected);
}
