using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.UserServices;

public static class SyncService
{
    internal const string REMOTE_FILE_FOLDER = "file";
    internal static readonly string LOCAL_FILE_FOLDER = Env.TemplateFileFolder;
    internal static Mutex remoteProfilemutex = new();
    public const string PULL_START_ENENT_NAME = "PULL_START_ENENT";
    public const string PULL_STOP_ENENT_NAME = "PULL_STOP_ENENT";
    public const string PUSH_START_ENENT_NAME = "PUSH_START_ENENT";
    public const string PUSH_STOP_ENENT_NAME = "PUSH_STOP_ENENT";
}