namespace SyncClipboard.Core.Commons
{
    public static class Env
    {
        public const string SoftName = "SyncClipboard";
        public const string HomePage = "https://github.com/Jeric-X/SyncClipboard";

        public const string RemoteProfilePath = "SyncClipboard.json";
        public static readonly string ProgramDirectory = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string AppDataDirectory = GetOrCreateFolder(GetAppDataDirectory());
        public static readonly string ProgramPath = Environment.ProcessPath ?? "";
        public static readonly string UserConfigFile = FullPath("SyncClipboard.json");
        public static readonly string AppDataFileFolder = GetOrCreateFolder(FullPath("file"));
        public static readonly string TemplateFileFolder = GetTemplateFileFolder();
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
            return appDataDirectory;
        }

        private static string GetOrCreateFolder(string path)
        {
            if (Directory.Exists(path) is false)
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        private static string? _templateFileFolder;
        private static string GetTemplateFileFolder()
        {
            if (_templateFileFolder is null)
            {
                _templateFileFolder = Path.Combine(AppDataFileFolder, DateTime.Now.ToString("yyyyMMdd"));
                Directory.CreateDirectory(_templateFileFolder);
            }
            return _templateFileFolder;
        }
    }
}