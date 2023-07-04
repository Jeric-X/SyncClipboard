using Microsoft.Win32;
using System.Windows.Forms;

namespace SyncClipboard.Module
{
    internal static class StartUpWithWindows
    {
        internal static void SetStartUp(bool startUpWithWindows)
        {
            try
            {
                if (startUpWithWindows)
                {
                    Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Env.SoftName, Application.ExecutablePath);
                }
                else
                {
                    Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true).DeleteValue(Env.SoftName, false);
                }
            }
            catch
            {
                // Log.Write("设置启动项失败");
            }
        }

        internal static bool Status()
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Env.SoftName, null) != null;
        }
    }
}
