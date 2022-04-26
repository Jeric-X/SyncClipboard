using SyncClipboard.Control;
using SyncClipboard.Module;
using SyncClipboard.Service;
using SyncClipboard.Utility;
using SyncClipboard.Utility.Web;

namespace SyncClipboard
{
    internal static class Global
    {
        internal static Utility.IWebDav WebDav;
        internal static Utility.Web.IWebDav WebDavClient;
        internal static Notifyer Notifyer;
        internal static MainController Menu;
        internal static ServiceManager ServiceManager;
        internal static string AppUserModelId;

        internal static void StartUp()
        {
            StartUpUserConfig();
            StartUpUI();
            LoadGlobalWebDavSession();
            AppUserModelId = Utility.Notification.Register.RegistFromCurrentProcess();
            ServiceManager = new ServiceManager();
            ServiceManager.StartUpAllService();
        }

        internal static void ReloadConfig()
        {
            ReloadUI();
            LoadGlobalWebDavSession();
            ServiceManager.LoadAllService();
        }

        internal static void EndUp()
        {
            ServiceManager?.StopAllService();
            Utility.Notification.Register.UnRegistFromCurrentProcess();
        }

        private static void LoadGlobalWebDavSession()
        {
            WebDav = new WebDav(
                UserConfig.Config.SyncService.RemoteURL,
                UserConfig.Config.SyncService.UserName,
                UserConfig.Config.SyncService.Password,
                UserConfig.Config.Program.IntervalTime,
                UserConfig.Config.Program.RetryTimes,
                UserConfig.Config.Program.TimeOut
            );

            WebDavClient = new WebDavClient(UserConfig.Config.SyncService.RemoteURL)
            {
                User = UserConfig.Config.SyncService.UserName,
                Token = UserConfig.Config.SyncService.Password,
                IntervalTime = UserConfig.Config.Program.IntervalTime,
                RetryTimes = UserConfig.Config.Program.RetryTimes,
                Timeout = UserConfig.Config.Program.TimeOut
            };

            WebDavClient.TestAlive().ContinueWith(
                (res) => Log.Write("[WebDavClient]" + res.Result.ToString()),
                System.Threading.Tasks.TaskContinuationOptions.NotOnFaulted
            );

            WebDav.TestAliveAsync().ContinueWith(
                (res) => Log.Write(res.Result.ToString()),
                System.Threading.Tasks.TaskContinuationOptions.NotOnFaulted
            );
        }

        private static void StartUpUI()
        {
            Menu = new MainController();
            Notifyer = Menu.Notifyer;
            ReloadUI();
        }

        private static void ReloadUI()
        {
            Menu.LoadConfig();
        }

        private static void StartUpUserConfig()
        {
            UserConfig.Load();
            UserConfig.ConfigChanged += ReloadConfig;
        }
    }
}
