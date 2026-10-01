using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.Commons;

public static partial class Env
{
    public const string LinuxPackageAppId = "xyz.jericx.desktop.syncclipboard";

    public static readonly string LinuxUserDesktopEntryFolder = UserPath(".local/share/applications");

    public static string? GetAppImageExecPath()
    {
        var path = Environment.GetEnvironmentVariable("APPIMAGE");
        var appDir = Environment.GetEnvironmentVariable("APPDIR");
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            return null;
        if (string.IsNullOrEmpty(appDir) || !Path.IsPathFullyQualified(appDir) || !Directory.Exists(appDir))
            return null;
        return FileSystem.IsWithin(ProgramDirectory, appDir) ? path : null;
    }
}
