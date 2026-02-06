namespace SyncClipboard.Core.Commons
{
    public static class Env
    {
        public const string SoftName = "SyncClipboard";
        public const string HomePage = "https://github.com/Jeric-X/SyncClipboard";
        public const string AppVersion = SyncClipboardProperty.AppVersion;

        public const string RequestServerVersion = "3.1.1";
        public const string UpdateApiUrl = "https://api.github.com/repos/Jeric-X/SyncClipboard/releases";
        public const string UpdateUrl = "https://github.com/Jeric-X/SyncClipboard/releases";

        public const string RuntimeConfigName = "RuntimeConfig.json";
        public const string UpdateInfoFile = "update_info.json";
        public const string LinuxPackageAppId = "xyz.jericx.desktop.syncclipboard";
        public static readonly string LinuxUserDesktopEntryFolder = UserPath(".local/share/applications");
        public static readonly string ProgramDirectory = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string AppDataDirectory = GetOrCreateFolder(GetAppDataDirectory());
        public static readonly string UserAppDataDirectory = GetUserAppDataDirectory();
        public static readonly string ProgramPath = GetProgramPath();
        public static readonly string UserConfigFile = FullPath("SyncClipboard.json");
        public static readonly string PortableUserConfigFile = Path.Combine(ProgramDirectory, "SyncClipboard.json");
        public static readonly string RuntimeConfigPath = FullPath(RuntimeConfigName);
        public static readonly string StaticConfigPath = Path.Combine(ProgramDirectory, "StaticConfig.json");
        public static readonly string UpdateInfoPath = Path.Combine(ProgramDirectory, UpdateInfoFile);
        public static readonly string AppDataFileFolder = GetOrCreateFolder(FullPath("file"));
        public static readonly string AppDataAssetsFolder = GetOrCreateFolder(FullPath("assets"));
        public static readonly string AppDataDbPath = GetOrCreateFolder(FullPath("data"));
        public static readonly string HistoryFileFolder = GetOrCreateFolder(Path.Combine(FullPath("file"), "history"));
        public static string TemplateFileFolder => GetTemplateFileFolder();
        public static string ImageTemplateFolder => Path.Combine(TemplateFileFolder, "temp images");
        public static readonly string LogFolder = FullPath("log");
        public static readonly string UpdateFolder = GetOrCreateFolder(FullPath("update"));

        public static string FullPath(string relativePath)
        {
            return Path.Combine(AppDataDirectory, relativePath);
        }

        private static string GetUserAppDataDirectory()
        {
            return Environment.GetFolderPath(
                   Environment.SpecialFolder.ApplicationData,
                   Environment.SpecialFolderOption.Create) ?? throw new Exception("Can not open system app data folder.");
        }

        public static string GetUserHomeFolder()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,
                   Environment.SpecialFolderOption.Create) ?? throw new Exception("Can not open user home folder.");
        }

        public static string UserPath(string path)
        {
            return Path.Combine(GetUserHomeFolder(), path);
        }

        private static string GetAppDataDirectory()
        {
            var appDataDirectory = Path.Combine(GetUserAppDataDirectory(), SoftName);
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
        private static DateTime? _dateTime;

        private static string GetTemplateFileFolder()
        {
            var dateTime = DateTime.Today;
            if (dateTime != _dateTime || _templateFileFolder is null)
            {
                _dateTime = dateTime;
                _templateFileFolder = Path.Combine(AppDataFileFolder, dateTime.ToString("yyyyMMdd"));
                Directory.CreateDirectory(_templateFileFolder);
            }
            return _templateFileFolder;
        }

        public static string? GetAppImageExecPath()
        {
            var ARGV0 = Environment.GetEnvironmentVariable("ARGV0");
            var APPDIR = Environment.GetEnvironmentVariable("APPDIR");
            var OWD = Environment.GetEnvironmentVariable("OWD");
            if (string.IsNullOrEmpty(ARGV0) is false &&
                string.IsNullOrEmpty(APPDIR) is false &&
                string.IsNullOrEmpty(OWD) is false)
            {
                return Path.GetFullPath(ARGV0);
            }
            return null;
        }

        private static string GetProgramPath()
        {
            if (OperatingSystem.IsLinux())
            {
                if (GetAppImageExecPath() is string appImagePath)
                {
                    return appImagePath;
                }
            }

            return Environment.ProcessPath ?? throw new Exception("Can not get program path.");
        }
    }
}