using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IRemoteClipboardServer : IDisposable
{
    Task<Profile> GetProfileAsync(CancellationToken cancellationToken = default);
    Task SetProfileAsync(Profile profile, CancellationToken cancellationToken = default);
    Task DownloadProfileDataAsync(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    event EventHandler<ProfileChangedEventArgs> RemoteProfileChanged;
    event EventHandler<PollStatusEventArgs> PollStatusEvent;
}

public class ProfileChangedEventArgs : EventArgs
{
    public Profile NewProfile { get; init; } = new UnknownProfile();
    public Profile OldProfile { get; init; } = new UnknownProfile();
}

public class PollStatusEventArgs : EventArgs
{
    public PollStatus Status { get; init; }
    public string? Message { get; init; }
    public Exception? Exception { get; init; }
}

public enum PollStatus
{
    StoppedDueToNetworkIssues,
    Resumed
}