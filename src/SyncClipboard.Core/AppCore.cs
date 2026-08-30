using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NativeNotification;
using NativeNotification.Interface;
using Quartz;
using SharpHook;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Options;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using SyncClipboard.Core.RemoteServer.Adapter.S3Server;
using SyncClipboard.Core.RemoteServer.Adapter.WebDavServer;
using SyncClipboard.Core.RemoteServer.LogInHelper;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.UserServices.ClipboardService;
using SyncClipboard.Core.UserServices.ServerService;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.FileCacheManager;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Job;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.Utilities.Network;
using SyncClipboard.Core.Utilities.Updater;
using SyncClipboard.Core.Utilities.Web;
using SyncClipboard.Core.ViewModels;
using System.Diagnostics;

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
            Logger = serviceProvider.GetRequiredService<Interfaces.ILogger>();
            SyncClipboardConfigRegistry.EnsureInitialized();
            AccountConfigRegistry.EnsureInitialized();
            ConfigManager = serviceProvider.GetRequiredService<ConfigManager>();
            var loggerOption = serviceProvider.GetRequiredService<LoggerOption>();
            ConfigManager.GetAndListenConfig<ProgramConfig>(config =>
            {
                loggerOption.FlushImmediately = config.DiagnoseMode;
            });

            _current = this;
        }

        public static async Task<AppCore?> CreateAsync(IServiceProvider serviceProvider)
        {
            var recoveryService = serviceProvider.GetRequiredService<ConfigRecoveryService>();
            return await recoveryService.ExecuteWithRecoveryAsync(
                () => new AppCore(serviceProvider),
                () =>
                {
                    var staticConfig = serviceProvider.GetRequiredService<StaticConfig>();
                    var portableUserConfig = staticConfig.GetConfig<EnvConfig>().PortableUserConfig;
                    return ConfigManager.GetConfigPath(portableUserConfig);
                },
                Strings.ApplicationStartupFailed,
                "Application startup");
        }

        private async void ReloadConfig()
        {
            while (true)
            {
                try
                {
                    ConfigManager.Reload();
                    return;
                }
                catch (Exception exception)
                {
                    var restored = await Services
                        .GetRequiredService<ConfigRecoveryService>()
                        .TryRestoreCurrentConfigAsync(
                            ConfigManager.Path,
                            ConfigManager.RestoreCurrentConfig,
                            exception);
                    if (!restored)
                    {
                        Services.GetRequiredService<IMainWindow>().ExitApp();
                        return;
                    }
                }
            }
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
            if (OperatingSystem.IsWindows())
            {
                Logger.Write(
                    LOG_TAG,
                    $"Running as administrator: {Env.IsRunningAsAdministrator}, " +
                    $"user in Administrators group: {Env.IsUserInAdministratorGroup}");
            }

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
            _ = Services.GetRequiredKeyedService<IWindow>("HistoryWindow");
            var historyViewModel = Services.GetRequiredService<HistoryViewModel>();

            AddSystemContextMenu(contextMenu, mainWindow, historyViewModel);
            RegisterForSystemHotkey(mainWindow, historyViewModel);

            ProxyManager.Init(configManager);

            ServiceManager = Services.GetRequiredService<ServiceManager>();
            ServiceManager.StartUpAllService();

            InitTrayIcon(historyViewModel);
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

        private void AddSystemContextMenu(
            IContextMenu contextMenu,
            IMainWindow mainWindow,
            HistoryViewModel historyViewModel)
        {
            contextMenu.AddMenuItem(new MenuItem(Strings.Settings, mainWindow.Show), "Top Group");
            contextMenu.AddMenuItem(new MenuItem(Strings.About, () => mainWindow.OpenPage(PageDefinition.About)), "Top Group");
            contextMenu.AddMenuItem(new MenuItem(Strings.HistoryPanel, historyViewModel.ShowWithAutoPosition), "Top Group");

            MenuItem[] menu =
            [
                new MenuItem(I18n.Strings.OpenConfigFile, () => Sys.OpenWithDefaultApp(ConfigManager.Path)),
                new MenuItem(I18n.Strings.ReloadConfigFile, ReloadConfig),
#if !MACOS
                new MenuItem(I18n.Strings.OpenInstallFolder, () => Sys.ShowPathInFileManager(Env.ProgramPath)),
#endif
                new MenuItem(I18n.Strings.OpenConfigFileFolder, () => Sys.OpenFolderInFileManager(Env.AppDataDirectory)),
            ];
            contextMenu.AddMenuItemGroup(menu);
        }

        private void RegisterForSystemHotkey(IMainWindow mainWindow, HistoryViewModel historyViewModel)
        {
            var hotkeyManager = Services.GetService<HotkeyManager>();
            if (hotkeyManager is null) return;

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
                        historyViewModel.ShowWithAutoPosition
                    ),
                    new UniqueCommand(
                        Strings.ToggleHistoryPanel,
                        "ToggleHistoryPanel",
                        historyViewModel.SwitchVisible
                    )
                }
            };

            hotkeyManager.RegisterCommands(CommandCollection);
        }

        private void InitTrayIcon(HistoryViewModel historyViewModel)
        {
            var trayIcon = Services.GetRequiredService<ITrayIcon>();
            var mainWindow = Services.GetRequiredService<IMainWindow>();
            trayIcon.LeftClicked += historyViewModel.ShowWithAutoPosition;
            trayIcon.DoubleClicked += mainWindow.Show;
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
            services.AddSingleton<ISyncClipboardConfigMigration, SyncClipboardConfigMigrationV0ToV1>();
            services.AddSingleton<SyncClipboardConfigUpgrader>();
            services.AddSingleton<ConfigRecoveryService>();
            services.AddSingleton<AccountManager>();
            services.AddSingleton<INetworkContextProvider, SystemNetworkContextProvider>();
            services.AddSingleton<StaticConfig>();
            services.AddKeyedTransient(Env.UpdateInfoFile, (sp, key) => new ConfigBase(Env.UpdateInfoPath, sp));
            services.AddKeyedSingleton(Env.RuntimeConfigName, (sp, key) => new ConfigBase(Env.RuntimeConfigPath, sp));
            services.AddSingleton<LoggerOption>();
            services.AddSingleton<Interfaces.ILogger, Logger>();
            services.AddSingleton<IMessenger, WeakReferenceMessenger>();
            services.AddSingleton<IEventSimulator, EventSimulator>();
            services.AddTransient<VirtualKeyboard>();
            services.AddSingleton<UpdateChecker>();
            services.AddSingleton<HistorySyncer>();
            services.AddSingleton<HistoryManager>();
            services.AddSingleton<HistorySyncer>();
            services.AddSingleton<HistoryTransferQueue>();

            services.AddSingleton<IHttp, Http>();
            services.AddSingleton<LocalFileCacheManager>();
            services.AddSingleton<RemoteClipboardServerFactory>();
            services.AddSingleton<ServiceManager>();
            services.AddSingleton<HotkeyManager>();
            services.AddSingleton<ForegroundWindowMonitor>();
            services.AddSingleton<IForegroundWindowMonitor>(sp => sp.GetRequiredService<ForegroundWindowMonitor>());
            services.AddTransient<ForegroundWindowCapture>();
            services.AddTransient<GithubUpdater>();
            services.AddQuartz();
            services.AddSingleton<IScheduler>(sp => sp.GetRequiredService<ISchedulerFactory>().GetScheduler().GetAwaiter().GetResult());
            services.AddTransient<AppInstance>();
            services.AddSingleton(sp => ManagerFactory.GetNotificationManager(
                new NativeNotificationOption
                {
                    AppName = Env.SoftName,
                    AppIcon = Path.Combine(Env.ProgramDirectory, "Assets", "icon.svg")
                }
            ));
            services.AddKeyedSingleton<INotification>("ProfileNotification", (sp, key) => sp.GetRequiredService<INotificationManager>().Create());
            services.AddSingleton<ProfileNotificationHelper>();

            services.AddServerAdapter<WebDavConfig, WebDavAdapter>();
            services.AddServerAdapter<OfficialConfig, OfficialAdapter>();
            services.AddServerAdapter<S3Config, S3Adapter>();
            services.AddLogInHelper<WebDavConfig, NextCloudLoginHelper>();
            services.AddSingleton<LocalClipboardSetter>();
            services.AddSingleton<ProfileActionBuilder>();
            services.AddSingleton<IProfileEnv, ClientProfileEnvProvider>();
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
            services.AddTransient<NetworkAccountSwitchViewModel>();
            services.AddTransient<CurrentNetworkStatusViewModel>();
            services.AddTransient<FileSyncFilterSettingViewModel>();
            services.AddSingleton<ClipboardOwnerFilterSettingViewModel>();
            services.AddTransient<ClipboardAcquisitionRulesViewModel>();
            services.AddTransient<ProxySettingViewModel>();
            services.AddSingleton<ServiceStatusViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<HotkeyViewModel>();
            services.AddSingleton<HotkeyBlacklistViewModel>();
            services.AddSingleton<HistoryViewModel>();
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
            services.AddSingleton<HistoryService>();
            services.AddSingleton<IService, HistoryService>(sp => sp.GetRequiredService<HistoryService>());
            services.AddSingleton<NetworkAccountSwitchService>();
            services.AddSingleton<IService>(sp => sp.GetRequiredService<NetworkAccountSwitchService>());
            services.AddSingleton<HotkeyBlacklistService>();
            services.AddSingleton<IService>(sp => sp.GetRequiredService<HotkeyBlacklistService>());
            services.AddSingleton<ForegroundWindowTrackingService>();
            services.AddSingleton<IService>(sp => sp.GetRequiredService<ForegroundWindowTrackingService>());
        }
    }
}
