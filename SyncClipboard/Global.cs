using System.IO;
using SyncClipboard.Control;
using SyncClipboard.Module;
using SyncClipboard.Service;
using SyncClipboard.Utility;
using SyncClipboard.Utility.Web;

namespace SyncClipboard
{
    internal static class Global
    {
        internal static IWebDav WebDav;
        internal static Notifyer Notifyer;
        internal static MainController Menu;
        internal static ServiceManager ServiceManager;
        internal static string AppUserModelId;

        internal static void StartUp()
        {
            StartUpUI();
            StartUpUserConfig();
            LoadGlobalWebDavSession();
            AppUserModelId = Utility.Notification.Register.RegistFromCurrentProcess();
            if (!Directory.Exists(Env.LOCAL_FILE_FOLDER))
            {
                Directory.CreateDirectory(Env.LOCAL_FILE_FOLDER);
            }
            ServiceManager = new ServiceManager();
            ServiceManager.StartUpAllService();
        }

        internal static void ReloadConfig()
        {
            ReloadUI();
            LoadGlobalWebDavSession();
            ServiceManager?.LoadAllService();
        }

        internal static void EndUp()
        {
            ServiceManager?.StopAllService();
            Utility.Notification.Register.UnRegistFromCurrentProcess();
        }

        private static void LoadGlobalWebDavSession()
        {
            WebDav = new WebDavClient(UserConfig.Config.SyncService.RemoteURL)
            {
                User = UserConfig.Config.SyncService.UserName,
                Token = UserConfig.Config.SyncService.Password,
                IntervalTime = UserConfig.Config.Program.IntervalTime,
                RetryTimes = UserConfig.Config.Program.RetryTimes,
                Timeout = UserConfig.Config.Program.TimeOut
            };

            WebDav.TestAlive().ContinueWith(
                (res) => Log.Write("[WebDavClient] test sucess = " + res.Result.ToString()),
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
            Menu?.LoadConfig();
        }

        private static void StartUpUserConfig()
        {
            UserConfig.ConfigChanged += ReloadConfig;
            UserConfig.Load();
            Menu.AddMenuItem(
                new string[] { "打开配置文件", "打开配置文件所在位置", "重新载入配置文件" },
                new System.Action[] {
                    () => {
                        var open = new System.Diagnostics.Process();
                        open.StartInfo.FileName = "notepad";
                        open.StartInfo.Arguments = Env.FullPath(UserConfig.CONFIG_FILE);
                        open.Start();
                    },
                    () => {
                        var open = new System.Diagnostics.Process();
                        open.StartInfo.FileName = "explorer";
                        open.StartInfo.Arguments = "/e,/select," + Env.FullPath(UserConfig.CONFIG_FILE);
                        open.Start();
                    },
                    () => UserConfig.Load()
                }
            );
        }
    }
}
