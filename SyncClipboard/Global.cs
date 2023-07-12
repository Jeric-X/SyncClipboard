using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Control;
using SyncClipboard.Core;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Module;
using SyncClipboard.Core.Commons;
using SyncClipboard.Utility;
using System;
using System.Windows.Forms;
using SyncClipboard.Service;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.ClipboardWinform;

namespace SyncClipboard
{
    public static class Global
    {
        internal static IWebDav WebDav;
        internal static Notifyer Notifyer;
        internal static ContextMenu Menu;
        internal static ServiceManager ServiceManager;
        internal static IServiceProvider Services;
        internal static IHttp Http;
        internal static Core.Commons.UserConfig UserConfig;
        internal static ILogger Logger;

        public static void StartUp()
        {
            ConfigurateServices();
            new ProgramWorkflow(Services).Run();
            StartUpUI();

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
            services.AddSingleton<IClipboardFactory, ClipboardFactory>();

            services.AddTransient<IClipboardSetter<TextProfile>, TextClipboardSetter>();
            services.AddTransient<IClipboardSetter<FileProfile>, FileClipboardSetter>();
            services.AddTransient<IClipboardSetter<ImageProfile>, ImageClipboardSetter>();

            ConfigurateProgramService(services);

            Services = services.BuildServiceProvider();

            ServiceManager = Services.GetRequiredService<ServiceManager>();
            Http = Services.GetRequiredService<IHttp>();
            WebDav = Services.GetRequiredService<IWebDav>();
            UserConfig = Services.GetRequiredService<Core.Commons.UserConfig>();
            Logger = Services.GetRequiredService<ILogger>();
        }

        private static void ConfigurateProgramService(IServiceCollection services)
        {
            services.AddSingleton<IService, CommandService>();
            services.AddSingleton<IService, ClipboardService>();
            services.AddSingleton<IService, UploadService>();
            services.AddSingleton<IService, DownloadService>();
            services.AddSingleton<IService, EasyCopyImageSerivce>();
            services.AddSingleton<IService, ConvertService>();
            services.AddSingleton<IService, ServerService>();
        }

        internal static void EndUp()
        {
            ServiceManager?.StopAllService();
        }

        private static void StartUpUI()
        {
            MenuItem[] menuItems =
            {
                new ToggleMenuItem("开机启动", StartUpWithWindows.Status(), StartUpWithWindows.SetStartUp),
                new MenuItem("从Nextcloud登录", Nextcloud.LogWithNextcloud),
                new MenuItem("检查更新", UpdateChecker.Check),
                new MenuItem("退出", Application.Exit)
            };
            Menu.AddMenuItemGroup(menuItems, reverse: true);
        }
    }
}
