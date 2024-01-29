using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;

namespace SyncClipboard.Core.Utilities.Notification
{
    // Modified from https://github.com/pr8x/DesktopNotifications
    public sealed class Register
    {
        [DllImport("shell32.dll", SetLastError = true)]
        private static extern void SetCurrentProcessExplicitAppUserModelID(
            [MarshalAs(UnmanagedType.LPWStr)] string appId);

        public static void UnRegistFromCurrentProcess()
        {
            ToastNotificationManagerCompat.Uninstall();
        }
        public static string RegistFromCurrentProcess(string? customName = null, string? appUserModelId = null)
        {
            var mainModule = Process.GetCurrentProcess().MainModule;

            if (mainModule?.FileName == null)
            {
                throw new InvalidOperationException("No valid process module found.");
            }

            var appName = customName ?? Path.GetFileNameWithoutExtension(mainModule.FileName);
            var aumid = appUserModelId ?? appName; //TODO: Add seeded bits to avoid collisions?

            SetCurrentProcessExplicitAppUserModelID(aumid);

            using var shortcut = new ShellLink
            {
                TargetPath = mainModule.FileName,
                Arguments = string.Empty,
                AppUserModelID = aumid
            };

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var startMenuPath = Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs");
            var shortcutFile = Path.Combine(startMenuPath, $"{appName}.lnk");

            shortcut.Save(shortcutFile);
            return aumid;
        }
    }
}