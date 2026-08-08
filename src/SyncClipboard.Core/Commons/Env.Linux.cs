using System.Runtime.Versioning;

namespace SyncClipboard.Core.Commons;

public static partial class Env
{
    public const string LinuxPackageAppId = "xyz.jericx.desktop.syncclipboard";

    public static readonly string LinuxUserDesktopEntryFolder = UserPath(".local/share/applications");

    public static string? GetAppImageExecPath()
    {
        var argv0 = Environment.GetEnvironmentVariable("ARGV0");
        var appDir = Environment.GetEnvironmentVariable("APPDIR");
        var owd = Environment.GetEnvironmentVariable("OWD");
        if (string.IsNullOrEmpty(argv0) is false &&
            string.IsNullOrEmpty(appDir) is false &&
            string.IsNullOrEmpty(owd) is false)
        {
            return Path.GetFullPath(argv0);
        }

        return null;
    }
}
