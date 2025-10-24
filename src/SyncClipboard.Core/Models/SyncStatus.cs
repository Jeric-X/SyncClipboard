namespace SyncClipboard.Core.Models;

public enum SyncStatus
{
    LocalOnly,
    ServerOnly,
    Synced,
    Disconnected,
    SyncError
}
