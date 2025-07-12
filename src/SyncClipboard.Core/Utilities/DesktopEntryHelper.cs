using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.Utilities;

public static class DesktopEntryHelper
{
    public static string GetLinuxDesktopEntryContent()
    {
        var fileName = $"{Env.LinuxPackageAppId}.desktop";
        var EmbeddedPath = Path.Combine(Env.ProgramDirectory, fileName);
        if (Env.GetAppImageExecPath() is string appImagePath)
        {
            var iconPath = Path.Combine(Env.AppDataAssetsFolder, "icon.svg");
            File.Copy(Path.Combine(Env.ProgramDirectory, "Assets", "icon.svg"), iconPath, true);
            var desktopContent = File.ReadAllText(EmbeddedPath)
                .Replace("/usr/bin/SyncClipboard.Desktop.Default", appImagePath)
                .Replace("env LANG=en_US.UTF-8 ", string.Empty)
                .Replace("Icon=xyz.jericx.desktop.syncclipboard", $"Icon={iconPath}");
            return desktopContent;
        }
        else
        {
            return File.ReadAllText(EmbeddedPath);
        }
    }

    public static void SetLinuxDesktopEntry(string folder)
    {
        var fileName = $"{Env.LinuxPackageAppId}.desktop";
        var filePath = Path.Combine(folder, fileName);

        if (Directory.Exists(folder) is false)
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(filePath, GetLinuxDesktopEntryContent());
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