using Microsoft.Win32;
using SyncClipboard.Core.Commons;
using System.Runtime.Versioning;

namespace SyncClipboard.Core.Utilities;

[SupportedOSPlatform("windows")]
public class StartUpHelper
{
    public static bool Status()
    {
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Env.SoftName, null);
        return path as string == Env.ProgramPath;
    }

    public static void Set(bool enabled)
    {
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
