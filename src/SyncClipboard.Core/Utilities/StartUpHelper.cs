using Microsoft.Win32;
using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.Utilities;

public class StartUpHelper
{
    public static bool Status()
    {
        if (!OperatingSystem.IsOSPlatform("windows"))
        {
            return false;
        }
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Env.SoftName, null);
        return path as string == Env.ProgramPath;
    }

    public static void Set(bool enabled)
    {
        if (!OperatingSystem.IsOSPlatform("windows"))
        {
            return;
        }

        if (enabled)
        {
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Env.SoftName, Env.ProgramPath);
        }
        else
        {
            Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)?.DeleteValue(Env.SoftName, false);
        }
    }
}
