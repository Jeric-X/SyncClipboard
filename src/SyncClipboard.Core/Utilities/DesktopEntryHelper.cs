using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.Utilities;

public static class DesktopEntryHelper
{
    public static void SetLinuxDesktopEntry(string folder)
    {
        var fileName = $"{Env.LinuxPackageAppId}.desktop";
        var filePath = Path.Combine(folder, fileName);

        var EmbeddedPath = Path.Combine(Env.ProgramDirectory, fileName);
        if (Env.GetAppImageExecPath() is string appImagePath)
        {
            var iconPath = Path.Combine(Env.AppDataAssetsFolder, "icon.svg");
            File.Copy(Path.Combine(Env.ProgramDirectory, "Assets", "icon.svg"), iconPath, true);
            var desktop = File.ReadAllText(EmbeddedPath)
                .Replace("/usr/bin/SyncClipboard.Desktop.Default", appImagePath)
                .Replace("Icon=xyz.jericx.desktop.syncclipboard", $"Icon={iconPath}");
            File.WriteAllText(filePath, desktop);
        }
        else
        {
            File.Copy(EmbeddedPath, filePath, true);
        }
    }

    public static void RemvoeLinuxDesktopEntry(string folder)
    {
        var fileName = $"{Env.LinuxPackageAppId}.desktop";
        var filePath = Path.Combine(folder, fileName);

        if (Env.GetAppImageExecPath() is not null)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}