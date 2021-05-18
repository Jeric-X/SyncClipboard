using SyncClipboard.Control;
using SyncClipboard.Module;
using SyncClipboard.Service;
using SyncClipboard.Utility;

namespace SyncClipboard
{
    internal static class Global
    {
        internal static IWebDav WebDav;
        internal static Notifyer Notifyer;
        internal static MainController Menu;
        internal static ServiceManager ServiceManager;

        internal static void StartUp()
        {
            StartUpUserConfig();
            StartUpUI();
            LoadGlobalWebDavSession();
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
