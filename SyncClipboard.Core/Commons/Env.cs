namespace SyncClipboard.Core.Commons
{
    public static class Env
    {
        public const string SoftName = "SyncClipboard";
        public const string HomePage = "https://github.com/Jeric-X/SyncClipboard";
        public const string VERSION = "2.0.1.2";
        public static readonly string Directory = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string ProgramPath = Environment.ProcessPath ?? "";
        public static readonly string UserConfigFile = FullPath("SyncClipboard.json");
        public static readonly string TemplateFileFolder = FullPath("file");
        public static readonly string LogFolder = FullPath("log");

        public static string FullPath(string relativePath)
        {
            return Path.Combine(Directory, relativePath);
        }
    }
}