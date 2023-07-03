using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Control;
using SyncClipboard.Core;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Module;
using SyncClipboard.Service;
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
            WebDav = Services.GetRequiredService<IWebDav>();
            UserConfig.InitializeUserConfig(Services.GetRequiredService<Core.Commons.UserConfig>());
        }

        internal static void ReloadConfig()
        {
            ReloadUI();
            ServiceManager?.LoadAllService();
        }

        internal static void EndUp()
        {
            ServiceManager?.StopAllService();
            Utility.Notification.Register.UnRegistFromCurrentProcess();
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
    }
}
