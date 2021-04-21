namespace SyncClipboard.Service
{
    internal class SyncService
    {
        internal const string REMOTE_RECORD_FILE = "SyncClipboard.json";
        internal const string REMOTE_FILE_FOLDER = "file";
        internal static readonly string LOCAL_FILE_FOLDER = Env.FullPath("file");
    }
}