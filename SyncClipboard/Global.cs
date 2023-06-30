using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Control;
using SyncClipboard.Core;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Module;
using SyncClipboard.Service;
using SyncClipboard.Utility;
using System;
using System.IO;

namespace SyncClipboard
{
    public static class Global
    {
        internal static IWebDav WebDav;
        internal static Notifyer Notifyer;
        internal static ContextMenu Menu;
        internal static ServiceManager ServiceManager;
        internal static string AppUserModelId;
        internal static IServiceProvider Services;
        internal static IHttp Http;

        public static void StartUp()
        {
            ConfigurateServices();
            new ProgramWorkflow(Services).Run();

            StartUpUI();
            StartUpUserConfig();
            LoadGlobalWebDavSession();
            PrepareWorkingFolder();
            AppUserModelId = Utility.Notification.Register.RegistFromCurrentProcess();
            ServiceManager = new ServiceManager();
            ServiceManager.StartUpAllService();
        }

        private static void ConfigurateServices()
        {
            var services = new ServiceCollection();
            ProgramWorkflow.ConfigCommonService(services);

            Notifyer = new Notifyer();
            Menu = new ContextMenu(Notifyer);
            services.AddSingleton<IContextMenu>(Menu);
            services.AddSingleton<ITrayIcon>(Notifyer);
            services.AddSingleton<IMainWindow, SettingsForm>();

            Services = services.BuildServiceProvider();
            Http = Services.GetRequiredService<IHttp>();
            UserConfig.InitializeUserConfig(Services.GetRequiredService<Core.Commons.UserConfig>());
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
            WebDav = Services.GetRequiredService<IWebDav>();

            WebDav.TestAlive().ContinueWith(
                (res) => Log.Write("[WebDavClient] test sucess = " + res.Result.ToString()),
                System.Threading.Tasks.TaskContinuationOptions.NotOnFaulted
            );
        }

        private static void StartUpUI()
        {
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
