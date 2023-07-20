using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Control;
using SyncClipboard.Core;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Module;
using SyncClipboard.Core.Commons;
using SyncClipboard.Utility;
using System;
using System.Windows.Forms;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.ClipboardWinform;

namespace SyncClipboard
{
    public static class Global
    {
        private static ContextMenu Menu;
        private static ServiceManager ServiceManager;

        internal static IHttp Http;
        internal static UserConfig UserConfig;
        internal static ILogger Logger;

        public static void StartUp()
        {
            var services = ConfigurateServices();

            ServiceManager = services.GetRequiredService<ServiceManager>();
            Http = services.GetRequiredService<IHttp>();
            UserConfig = services.GetRequiredService<Core.Commons.UserConfig>();
            Logger = services.GetRequiredService<ILogger>();

            new ProgramWorkflow(services).Run();
            StartUpUI();

            ServiceManager.StartUpAllService();
        }

        public static IServiceProvider ConfigurateServices()
        {
            var services = new ServiceCollection();
            ProgramWorkflow.ConfigCommonService(services);

            var notifyer = new Notifyer();
            Menu = new ContextMenu(notifyer);
            services.AddSingleton<IContextMenu>(Menu);
            services.AddSingleton<ITrayIcon>(notifyer);
            services.AddSingleton<IMainWindow, SettingsForm>();
            services.AddSingleton<IClipboardFactory, ClipboardFactory>();
            services.AddSingleton<IClipboardChangingListener, ClipboardListener>();

            services.AddTransient<IClipboardSetter<TextProfile>, TextClipboardSetter>();
            services.AddTransient<IClipboardSetter<FileProfile>, FileClipboardSetter>();
            services.AddTransient<IClipboardSetter<ImageProfile>, ImageClipboardSetter>();

            ConfigurateProgramService(services);

            return services.BuildServiceProvider();
        }

        private static void ConfigurateProgramService(IServiceCollection services)
        {
            services.AddSingleton<IService, CommandService>();
            // services.AddSingleton<IService, ClipboardService>();
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
