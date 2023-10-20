using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.ClipboardWinform;
using SyncClipboard.Control;
using SyncClipboard.Core;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Notification;
using SyncClipboard.Utility;
using System;
using System.Windows.Forms;

namespace SyncClipboard
{
    public static class Global
    {
        private static ContextMenu Menu;
        private static ProgramWorkflow ProgramWorkflow;

        internal static IHttp Http;
        internal static ConfigManager ConfigManager;
        internal static ILogger Logger;

        public static void StartUp()
        {
            var services = ConfigurateServices();

            Http = services.GetRequiredService<IHttp>();
            ConfigManager = services.GetRequiredService<ConfigManager>();
            Logger = services.GetRequiredService<ILogger>();

            ProgramWorkflow = new ProgramWorkflow(services);
            ProgramWorkflow.Run();
            StartUpUI();
        }

        public static IServiceProvider ConfigurateServices()
        {
            var services = new ServiceCollection();
            ProgramWorkflow.ConfigCommonService(services);
            ProgramWorkflow.ConfigurateUserService(services);

            var notifyer = new Notifyer();
            Menu = new ContextMenu(notifyer);

            services.AddTransient<IAppConfig, AppConfig>();

            services.AddSingleton<IContextMenu>(Menu);
            services.AddSingleton<ITrayIcon>(notifyer);
            services.AddSingleton<IMainWindow, SettingsForm>();
            services.AddSingleton<ClipboardFactory>();
            services.AddSingleton<IClipboardFactory>(sp => sp.GetRequiredService<ClipboardFactory>());
            services.AddSingleton<IProfileDtoHelper>(sp => sp.GetRequiredService<ClipboardFactory>());
            services.AddSingleton<IClipboardChangingListener, ClipboardListener>();
            services.AddSingleton<INotification, NotificationManager>();

            services.AddTransient<IClipboardSetter<TextProfile>, TextClipboardSetter>();
            services.AddTransient<IClipboardSetter<FileProfile>, FileClipboardSetter>();
            services.AddTransient<IClipboardSetter<ImageProfile>, ImageClipboardSetter>();

            return services.BuildServiceProvider();
        }

        internal static void EndUp()
        {
            ProgramWorkflow.Stop();
        }

        private static void StartUpUI()
        {
            MenuItem[] menuItems =
            {
                new ToggleMenuItem("开机启动", StartUpHelper.Status(), StartUpHelper.Set),
                new MenuItem("从Nextcloud登录", Nextcloud.LogWithNextcloud),
                new MenuItem("检查更新", Module.UpdateChecker.Check),
                new MenuItem("退出", Application.Exit)
            };
            Menu.AddMenuItemGroup(menuItems);
        }
    }
}
