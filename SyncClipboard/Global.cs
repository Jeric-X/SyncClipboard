using System;
using System.IO;
using SyncClipboard.Control;
using SyncClipboard.Module;
using SyncClipboard.Service;
using SyncClipboard.Utility;
using SyncClipboard.Utility.Web;

namespace SyncClipboard
{
    public static class Global
    {
        internal static IWebDav WebDav;
        internal static Notifyer Notifyer;
        internal static MainController Menu;
        internal static ServiceManager ServiceManager;
        internal static string AppUserModelId;

        public static void StartUp()
        {
            StartUpUI();
            StartUpUserConfig();
            LoadGlobalWebDavSession();
            PrepareWorkingFolder();
            AppUserModelId = Utility.Notification.Register.RegistFromCurrentProcess();
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
            Menu.AddMenuItemGroup(
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

        private static void PrepareWorkingFolder()
        {
            if (Directory.Exists(Env.LOCAL_FILE_FOLDER))
            {
                if (UserConfig.Config.Program.DeleteTempFilesOnStartUp)
                {
                    Directory.Delete(Env.LOCAL_FILE_FOLDER);
                    Directory.CreateDirectory(Env.LOCAL_FILE_FOLDER);
                }
            }
            else
            {
                Directory.CreateDirectory(Env.LOCAL_FILE_FOLDER);
            }

            var logFolder = new DirectoryInfo(Env.LOCAL_LOG_FOLDER);
            if (logFolder.Exists && UserConfig.Config.Program.LogRemainDays != 0)
            {
                var today = DateTime.Today;
                foreach (var log in logFolder.EnumerateFileSystemInfos("????????.txt"))
                {
                    var createTime = log.CreationTime.Date;
                    if ((today - createTime) > TimeSpan.FromDays(UserConfig.Config.Program.LogRemainDays))
                    {
                        log.Delete();
                    }
                }
            }
        }
    }
}
