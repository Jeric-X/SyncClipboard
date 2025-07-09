using System.Runtime.Versioning;
using Microsoft.Win32;
using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.Utilities;

public class StartUpHelper
{
    public static bool Status()
    {
        if (OperatingSystem.IsOSPlatform("windows"))
        {
            return CheckWindows();
        }
        else if (OperatingSystem.IsOSPlatform("linux"))
        {
            return CheckLinux();
        }
        else
        {
            throw new PlatformNotSupportedException("This method is only supported on Windows and Linux.");
        }
    }

    public static void Set(bool enable)
    {
        if (OperatingSystem.IsOSPlatform("windows"))
        {
            SetWindows(enable);
        }
        else if (OperatingSystem.IsOSPlatform("linux"))
        {
            SetLinux(enable);
        }
        else
        {
            throw new PlatformNotSupportedException("This method is only supported on Windows and Linux.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindows(bool enable)
    {
        if (enable)
        {
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Env.SoftName, Env.ProgramPath);
        }
        else
        {
            Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)?.DeleteValue(Env.SoftName, false);
        }
    }

    [SupportedOSPlatform("linux")]
    private static void SetLinux(bool enable)
    {
        var desktopFileName = $"{Env.LinuxPackageAppId}.desktop";
        var autoStartFolder = Path.Combine(Env.UserAppDataDirectory, "autostart");
        var autoStartDestkopFilePath = Path.Combine(autoStartFolder, desktopFileName);
        if (enable)
        {
            if (Directory.Exists(autoStartFolder) is false)
            {
                Directory.CreateDirectory(autoStartFolder);
            }

            var EmbeddedPath = Path.Combine(Env.ProgramDirectory, desktopFileName);
            if (Env.GetAppImageExecPath() is string appImagePath)
            {
                var desktop = File.ReadAllText(EmbeddedPath)
                    .Replace("/usr/bin/SyncClipboard.Desktop.Default", appImagePath);
                File.WriteAllText(autoStartDestkopFilePath, desktop);
            }
            else
            {
                File.Copy(EmbeddedPath, autoStartDestkopFilePath, true);
            }
        }
        else
        {
            if (File.Exists(autoStartDestkopFilePath))
            {
                File.Delete(autoStartDestkopFilePath);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckWindows()
    {
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Env.SoftName, null);
        return path as string == Env.ProgramPath;
    }

    [SupportedOSPlatform("linux")]
    private static bool CheckLinux()
    {
        var desktopFileName = $"{Env.LinuxPackageAppId}.desktop";
        var autoStartFolder = Path.Combine(Env.UserAppDataDirectory, "autostart");
        var autoStartDestkopFilePath = Path.Combine(autoStartFolder, desktopFileName);
        return File.Exists(autoStartDestkopFilePath);
    }
}
