using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using SharpHook;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.UserServices.ServerService;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Job;
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
        public INotification Notification => Services.GetRequiredService<INotification>();
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
            if (OperatingSystem.IsLinux() && Env.GetAppImageExecPath() is not null)
            {
                var runTimeConfig = Services.GetRequiredKeyedService<ConfigBase>(Env.RuntimeConfigName);
                var linuxRuntimeConfig = runTimeConfig.GetConfig<LinuxRuntimeConfig>();
                if (linuxRuntimeConfig.AppImageEntryInited is false)
                {
                    try
                    {
                        DesktopEntryHelper.SetLinuxDesktopEntry(Env.LinuxUserDesktopEntryFolder);
                        runTimeConfig.SetConfig(linuxRuntimeConfig with { AppImageEntryInited = true });
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

            contextMenu.AddMenuItem(new MenuItem(Strings.Settings, mainWindow.Show), "Top Group");
            contextMenu.AddMenuItem(new MenuItem(Strings.About, () => mainWindow.OpenPage(PageDefinition.About)), "Top Group");
            contextMenu.AddMenuItemGroup(configManager.Menu);

            ProxyManager.Init(configManager);
            SetUpRemoteWorkFolder();

            ServiceManager = Services.GetRequiredService<ServiceManager>();
            ServiceManager.StartUpAllService();

            RegisterForSystemHotkey(mainWindow);
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
                Notification.SendText("Can't restart application.", "Can't get program path");
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
                Notification.SendText("Can't restart application.", ex.Message);
            }
        }


        private void RegisterForSystemHotkey(IMainWindow mainWindow)
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
            mainWindow.ChangeTheme(config.Theme);
            if (config.HideWindowOnStartup is false)
            {
                mainWindow.Show();
            }
        }

        public void Stop()
        {
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
            services.AddSingleton<StaticConfig>();
            services.AddKeyedTransient(Env.UpdateInfoFile, (sp, key) => new ConfigBase(Env.UpdateInfoPath, sp));
            services.AddKeyedSingleton(Env.RuntimeConfigName, (sp, key) => new ConfigBase(Env.RuntimeConfigPath, sp));
            services.AddSingleton<Interfaces.ILogger, Logger>();
            services.AddSingleton<IMessenger, WeakReferenceMessenger>();
            services.AddSingleton<IEventSimulator, EventSimulator>();
            services.AddSingleton<UpdateChecker>();

            services.AddSingleton<IWebDav, WebDavClient>();
            services.AddSingleton<IHttp, Http>();
            services.AddSingleton<ServiceManager>();
            services.AddSingleton<HotkeyManager>();
            services.AddTransient<GithubUpdater>();
            services.AddQuartz();
            services.AddSingleton<IScheduler>(sp => sp.GetRequiredService<ISchedulerFactory>().GetScheduler().GetAwaiter().GetResult());
            services.AddTransient<AppInstance>();
        }

        public static void ConfigurateViewModels(IServiceCollection services)
        {
            services.AddTransient<SyncSettingViewModel>();
            services.AddTransient<SystemSettingViewModel>();
            services.AddTransient<AboutViewModel>();
            services.AddTransient<CliboardAssistantViewModel>();
            services.AddTransient<NextCloudLogInViewModel>();
            services.AddTransient<FileSyncFilterSettingViewModel>();
            services.AddTransient<ProxySettingViewModel>();
            services.AddSingleton<ServiceStatusViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<HotkeyViewModel>();
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
        }

        private async void SetUpRemoteWorkFolder()
        {
            try
            {
                var webdav = Services.GetRequiredService<IWebDav>();
                var res = await webdav.TestAlive();
                if (res)
                {
                    await webdav.CreateDirectory(Env.RemoteFileFolder);
                }
            }
            catch { }
        }
    }
}