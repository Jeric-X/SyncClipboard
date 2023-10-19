namespace SyncClipboard.Core.UserServices;

public static class SyncService
{
    internal static SemaphoreSlim remoteProfilemutex = new(1, 1);
    public const string ContextMenuGroupName = "Sync Service";
    public const string PULL_START_ENENT_NAME = "PULL_START_ENENT";
    public const string PULL_STOP_ENENT_NAME = "PULL_STOP_ENENT";
    public const string PUSH_START_ENENT_NAME = "PUSH_START_ENENT";
    public const string PUSH_STOP_ENENT_NAME = "PUSH_STOP_ENENT";
}