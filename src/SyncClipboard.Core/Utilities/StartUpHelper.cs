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
        var autoStartFolder = Path.Combine(Env.UserAppDataDirectory, "autostart");
        if (enable)
        {
            if (Directory.Exists(autoStartFolder) is false)
            {
                Directory.CreateDirectory(autoStartFolder);
            }

            DesktopEntryHelper.SetLinuxDesktopEntry(autoStartFolder);
        }
        else
        {
            DesktopEntryHelper.RemvoeLinuxDesktopEntry(autoStartFolder);
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
        var fileInfo = new FileInfo(autoStartDestkopFilePath);
        if (fileInfo.Exists is false)
        {
            return false;
        }

        if (fileInfo.Length > 1024 * 1024)  // 1Mb
        {
            return false;
        }

        return File.ReadLines(autoStartDestkopFilePath).Any(line => line == $"TryExec={Env.ProgramPath}");
    }
}
