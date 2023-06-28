namespace SyncClipboard.Core.Commons
{
    public static class Env
    {
        public const string SoftName = "SyncClipboard";
        public const string VERSION = "1.7.1";
        public static readonly string Directory = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string UserConfigFile = FullPath("SyncClipboard.json");
        //public static readonly string LOCAL_FILE_FOLDER = FullPath("file");
        public static readonly string LogFolder = FullPath("log");

        public static string FullPath(string relativePath)
        {
            return Path.Combine(Directory, relativePath);
        }
    }
}