namespace SyncClipboard.Core.Commons
{
    public static class Env
    {
        public const string SoftName = "SyncClipboard";
        public const string HomePage = "https://github.com/Jeric-X/SyncClipboard";
        public const string VERSION = "2.3.0";
        public static readonly string Directory = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string AppDataDirectory = GetAppDataDirectory();
        public static readonly string ProgramPath = Environment.ProcessPath ?? "";
        public static readonly string UserConfigFile = FullPath("SyncClipboard.json");
        public static readonly string TemplateFileFolder = FullPath("file");
        public static readonly string RemoteFileFolder = "file";
        public static readonly string LogFolder = FullPath("log");

        public static string FullPath(string relativePath)
        {
            return Path.Combine(AppDataDirectory, relativePath);
        }

        private static string GetAppDataDirectory()
        {
            var appDataParent = Environment.GetFolderPath(
                   Environment.SpecialFolder.ApplicationData,
                   Environment.SpecialFolderOption.Create) ?? throw new Exception("Can not open system app data folder.");
            var appDataDirectory = Path.Combine(appDataParent, SoftName);
            if (System.IO.Directory.Exists(appDataDirectory) is false)
            {
                System.IO.Directory.CreateDirectory(appDataDirectory);
            }
            return appDataDirectory;
        }
    }
}