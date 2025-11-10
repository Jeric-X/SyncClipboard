using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NativeNotification;
using NativeNotification.Interface;
using Quartz;
using SharpHook;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.UserServices.ClipboardService;
using SyncClipboard.Core.UserServices.ServerService;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.FileCacheManager;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Job;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.Utilities.Updater;
using SyncClipboard.Core.Utilities.Web;
using SyncClipboard.Core.ViewModels;
using System.Diagnostics;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.RemoteServer.Adapter.WebDavServer;
using SyncClipboard.Core.RemoteServer.LogInHelper;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using SyncClipboard.Core.Clipboard;

namespace SyncClipboard.Core
{
    public class AppCore
    {
        private const string LOG_TAG = "AppCore";
        private static AppCore? _current;
        public static AppCore Current => _current ?? throw new Exception("Appcore is not initialized");
        public static AppCore? TryGetCurrent() => _current;
        public IServiceProvider Services { get; }
        public Interfaces.ILogger Logger { get; }
        public ITrayIcon TrayIcon => Services.GetRequiredService<ITrayIcon>();
        public INotificationManager NotificationManager => Services.GetRequiredService<INotificationManager>();
        public ConfigManager ConfigManager { get; }

        private ServiceManager? ServiceManager { get; set; }

        public AppCore(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
            _current = this;
            Logger = serviceProvider.GetRequiredService<Interfaces.ILogger>();
            ConfigManager = serviceProvider.GetRequiredService<ConfigManager>();
        }

        private static void InitLanguage(ConfigManager configManager)
        {
            var langTag = configManager.GetConfig<ProgramConfig>().Language;
            if (string.IsNullOrEmpty(langTag))
            {
                return;
            }
            I18nHelper.SetProgramLanguage(langTag);
        }

        private void LogEnvInfo()
        {
            var appConfig = Services.GetRequiredService<IAppConfig>();
            Logger.Write(LOG_TAG, $"App core started, app name '{appConfig.AppStringId}', version '{appConfig.AppVersion}'");
            if (OperatingSystem.IsLinux())
            {
                Logger.Write(LOG_TAG, $"DISPLAY:{Environment.GetEnvironmentVariable("DISPLAY")}");
                Logger.Write(LOG_TAG, $"WAYLAND_DISPLAY:{Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")}");
                Logger.Write(LOG_TAG, $"ARGV0:{Environment.GetEnvironmentVariable("ARGV0")}");
                Logger.Write(LOG_TAG, $"APPIMAGE:{Environment.GetEnvironmentVariable("APPIMAGE")}");
                Logger.Write(LOG_TAG, $"OWD:{Environment.GetEnvironmentVariable("OWD")}");
            }
        }

        private void InitAppImageEntry()
        {
            if (OperatingSystem.IsLinux() && Env.GetAppImageExecPath() is string appImagePath)
            {
                var runTimeConfig = Services.GetRequiredKeyedService<ConfigBase>(Env.RuntimeConfigName);
                var linuxRuntimeConfig = runTimeConfig.GetConfig<LinuxRuntimeConfig>();
                if (linuxRuntimeConfig.AppImageEntryPath != appImagePath)
                {
                    try
                    {
                        DesktopEntryHelper.SetLinuxDesktopEntry(Env.LinuxUserDesktopEntryFolder);
                        runTimeConfig.SetConfig(linuxRuntimeConfig with { AppImageEntryPath = appImagePath });
                    }
                    catch { }
                }
            }
        }

        public void Run()
        {
            LogEnvInfo();
            InitAppImageEntry();
            var configManager = Services.GetRequiredService<ConfigManager>();
            InitLanguage(configManager);

            var contextMenu = Services.GetRequiredService<IContextMenu>();
            var mainWindow = Services.GetRequiredService<IMainWindow>();
            var historyWindow = Services.GetRequiredKeyedService<IWindow>("HistoryWindow");

            AddSystemContextMenu(contextMenu, mainWindow, historyWindow);
            RegisterForSystemHotkey(mainWindow);

            ProxyManager.Init(configManager);

            ServiceManager = Services.GetRequiredService<ServiceManager>();
            ServiceManager.StartUpAllService();

            InitTrayIcon();
            Services.GetRequiredService<AppInstance>().WaitForOtherInstanceToActiveAsync();
            contextMenu.AddMenuItemGroup([new(Strings.RestartApp, RestartApp), new(Strings.Exit, mainWindow.ExitApp)]);
            ShowMainWindow(configManager, mainWindow);
            RunStartUpCommands();
            Job.SetUpSchedulerJobs(Services);
        }

        private void RunStartUpCommands()
        {
            var HotkeyManager = Services.GetRequiredService<HotkeyManager>();

            foreach (var arg in Environment.GetCommandLineArgs())
            {
                Logger.Write(LOG_TAG, $"command arg: {arg}");
                if (arg.StartsWith(StartArguments.CommandPrefix))
                {
                    HotkeyManager.RunCommand(arg[StartArguments.CommandPrefix.Length..]);
                }
            }
        }

        private void RestartApp()
        {
            if (string.IsNullOrEmpty(Env.ProgramPath))
            {
                NotificationManager.ShowText("Can't restart application.", "Can't get program path.");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = Env.ProgramPath,
                UseShellExecute = true,
                Arguments = StartArguments.ShutdownPrivious
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                NotificationManager.ShowText("Can't restart application.", ex.Message);
            }
        }

        private void AddSystemContextMenu(IContextMenu contextMenu, IMainWindow mainWindow, IWindow historyWindow)
        {
            contextMenu.AddMenuItem(new MenuItem(Strings.Settings, mainWindow.Show), "Top Group");
            contextMenu.AddMenuItem(new MenuItem(Strings.About, () => mainWindow.OpenPage(PageDefinition.About)), "Top Group");
            contextMenu.AddMenuItem(new MenuItem(Strings.HistoryPanel, historyWindow.Focus), "Top Group");

            MenuItem[] menu =
            [
                new MenuItem(I18n.Strings.OpenConfigFile, () => Sys.OpenWithDefaultApp(ConfigManager.Path)),
                new MenuItem(I18n.Strings.ReloadConfigFile, ConfigManager.Load),
#if !MACOS
                new MenuItem(I18n.Strings.OpenInstallFolder, () => Sys.ShowPathInFileManager(Env.ProgramPath)),
#endif
                new MenuItem(I18n.Strings.OpenConfigFileFolder, () => Sys.OpenFolderInFileManager(Env.AppDataDirectory)),
            ];
            contextMenu.AddMenuItemGroup(menu);
        }

        private void RegisterForSystemHotkey(IMainWindow mainWindow)
        {
            var hotkeyManager = Services.GetService<HotkeyManager>();
            if (hotkeyManager is null) return;

            var HistoryWindow = Services.GetRequiredKeyedService<IWindow>("HistoryWindow");

            UniqueCommandCollection CommandCollection = new(Strings.System, PageDefinition.SystemSetting.FontIcon!)
            {
                Commands = {
                    new UniqueCommand(
                        Strings.OpenMainUI,
                        "6DB18835-1DAD-0495-E126-45F5D2D193A7",
                        mainWindow.Show
                    ),
                    new UniqueCommand(
                        Strings.CompletelyExit,
                        "2F30872E-B412-F580-7C20-F0D063A85BE0",
                        mainWindow.ExitApp
                    ),
                    new UniqueCommand(
                        Strings.OpenHistoryPanel,
                        "OpenHistoryPanel",
                        HistoryWindow.Focus
                    )
                }
            };

            hotkeyManager.RegisterCommands(CommandCollection);
        }

        private void InitTrayIcon()
        {
            var trayIcon = Services.GetRequiredService<ITrayIcon>();
            trayIcon.MainWindowWakedUp += Services.GetRequiredService<IMainWindow>().Show;
            trayIcon.Create();
        }

        private static void ShowMainWindow(ConfigManager configManager, IMainWindow mainWindow)
        {
            var config = configManager.GetConfig<ProgramConfig>();

            mainWindow.SetFont(config.Font);
            if (config.HideWindowOnStartup is false)
            {
                mainWindow.Show();
            }
        }

        public void Stop()
        {
            NotificationManager.RomoveAllNotifications();
            ServiceManager?.StopAllService();
            var disposable = Services as IDisposable;
            disposable?.Dispose();
        }

        public static void ConfigCommonService(IServiceCollection services)
        {
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Warning);
            });
            services.AddSingleton((serviceProvider) => serviceProvider);
            services.AddSingleton<ConfigManager>();
            services.AddSingleton<AccountManager>();
            services.AddSingleton<StaticConfig>();
            services.AddKeyedTransient(Env.UpdateInfoFile, (sp, key) => new ConfigBase(Env.UpdateInfoPath, sp));
            services.AddKeyedSingleton(Env.RuntimeConfigName, (sp, key) => new ConfigBase(Env.RuntimeConfigPath, sp));
            services.AddSingleton<Interfaces.ILogger, Logger>();
            services.AddSingleton<IMessenger, WeakReferenceMessenger>();
            services.AddSingleton<IEventSimulator, EventSimulator>();
            services.AddTransient<VirtualKeyboard>();
            services.AddSingleton<UpdateChecker>();
            services.AddSingleton<HistorySyncer>();
            services.AddSingleton<HistoryManager>();
            services.AddSingleton<HistorySyncer>();

            services.AddSingleton<IHttp, Http>();
            services.AddSingleton<LocalFileCacheManager>();
            services.AddSingleton<RemoteClipboardServerFactory>();
            services.AddSingleton<ServiceManager>();
            services.AddSingleton<HotkeyManager>();
            services.AddTransient<GithubUpdater>();
            services.AddQuartz();
            services.AddSingleton<IScheduler>(sp => sp.GetRequiredService<ISchedulerFactory>().GetScheduler().GetAwaiter().GetResult());
            services.AddTransient<AppInstance>();
            services.AddSingleton(sp => ManagerFactory.GetNotificationManager(
                new NativeNotificationOption
                {
                    AppName = Env.SoftName,
                    RemoveNotificationOnContentClick = false,
                    AppIcon = Path.Combine(Env.ProgramDirectory, "Assets", "icon.svg")
                }
            ));
            services.AddKeyedSingleton<INotification>("ProfileNotification", (sp, key) => sp.GetRequiredService<INotificationManager>().Create());
            services.AddSingleton<ProfileNotificationHelper>();

            services.AddServerAdapter<WebDavConfig, WebDavAdapter>();
            services.AddServerAdapter<OfficialConfig, OfficialAdapter>();
            services.AddLogInHelper<WebDavConfig, NextCloudLoginHelper>();
            services.AddSingleton<LocalClipboardSetter>();
            services.AddSingleton<ProfileActionBuilder>();
        }

        public static void ConfigurateViewModels(IServiceCollection services)
        {
            services.AddTransient<SyncSettingViewModel>();
            services.AddTransient<ServerConfigViewModel>();
            services.AddTransient<SystemSettingViewModel>();
            services.AddTransient<AboutViewModel>();
            services.AddTransient<CliboardAssistantViewModel>();
            services.AddTransient<NextCloudLogInViewModel>();
            services.AddTransient<AddAccountViewModel>();
            services.AddTransient<AccountConfigEditViewModel>();
            services.AddTransient<FileSyncFilterSettingViewModel>();
            services.AddTransient<ProxySettingViewModel>();
            services.AddSingleton<ServiceStatusViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<HotkeyViewModel>();
            services.AddTransient<HistoryViewModel>();
            services.AddTransient<HistorySettingViewModel>();
        }

        public static void ConfigurateUserService(IServiceCollection services)
        {
            services.AddSingleton<IService, EasyCopyImageSerivce>();
            services.AddSingleton<IService, ConvertService>();
            services.AddSingleton<IService, ServerService>();
            services.AddSingleton<UploadService>();
            services.AddSingleton<IService, UploadService>(sp => sp.GetRequiredService<UploadService>());
            services.AddSingleton<DownloadService>();
            services.AddSingleton<IService, DownloadService>(sp => sp.GetRequiredService<DownloadService>());
            services.AddSingleton<IService, HistoryService>();
        }
    }
}