namespace SyncClipboard.Core.Utilities.Updater;

public enum UpdaterState
{
    Idle,
    CheckingForUpdate,
    UpdateAvailable,
    UpdateAvailableAt3rdPartySrc,
    ReadyForDownload,
    UpToDate,
    Downloading,
    Downloaded,
    Failed,
    Canceled,
}
