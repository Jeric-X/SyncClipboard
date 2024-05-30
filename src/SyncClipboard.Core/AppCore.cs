using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SharpHook;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Updater;
using SyncClipboard.Core.Utilities.Web;
using SyncClipboard.Core.ViewModels;
using System.Globalization;

namespace SyncClipboard.Core
{
    public class AppCore
    {
        private static AppCore? _current;
        public static AppCore Current => _current ?? throw new Exception("Appcore is not initialized");
        public IServiceProvider Services { get; }
        public ILogger Logger { get; }

        private ServiceManager? ServiceManager { get; set; }

        public AppCore(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
            _current = this;
            Logger = serviceProvider.GetRequiredService<ILogger>();
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

        public void Run()
        {
            var configManager = Services.GetRequiredService<ConfigManager>();
            InitLanguage(configManager);

            var contextMenu = Services.GetRequiredService<IContextMenu>();
            var mainWindow = Services.GetRequiredService<IMainWindow>();

            contextMenu.AddMenuItem(new MenuItem(Strings.Settings, mainWindow.Show), "Top Group");
            contextMenu.AddMenuItemGroup(configManager.Menu);

            PrepareRemoteWorkingFolder();
            DelegateExtention.InvokeNoExcept(() => PrepareWorkingFolder(configManager));
            ServiceManager = Services.GetRequiredService<ServiceManager>();
            ServiceManager.StartUpAllService();

            RegisterForSystemHotkey(mainWindow);
            InitTrayIcon();
            Services.GetRequiredService<AppInstance>().WaitForOtherInstanceToActiveAsync();
            contextMenu.AddMenuItemGroup(new MenuItem[] { new(Strings.Exit, mainWindow.ExitApp) });
            ShowMainWindow(configManager, mainWindow);
            CheckUpdate();
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
                        Guid.Parse("6DB18835-1DAD-0495-E126-45F5D2D193A7"),
                        mainWindow.Show
                    ),
                    new UniqueCommand(
                        Strings.CompletelyExit,
                        Guid.Parse("2F30872E-B412-F580-7C20-F0D063A85BE0"),
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

        private async void CheckUpdate()
        {
            var configManager = Services.GetRequiredService<ConfigManager>();
            var updateChecker = Services.GetRequiredService<GithubUpdater>();
            var notificationManager = Services.GetService<INotification>();
            if (notificationManager is null)
            {
                return;
            }

            bool checkOnStartup = configManager.GetConfig<ProgramConfig>().CheckUpdateOnStartUp;
            if (checkOnStartup)
            {
                try
                {
                    var (needUpdate, newVersion) = await updateChecker.Check();
                    if (needUpdate)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        notificationManager.SendText(
                            Strings.FoundNewVersion,
                            $"v{updateChecker.Version} -> {newVersion.TagName}",
                            new Button(Strings.OpenDownloadPage, () => Sys.OpenWithDefaultApp(newVersion.HtmlUrl))
                        );
                    }
                }
                catch
                {
                }
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
            services.AddSingleton((serviceProvider) => serviceProvider);
            services.AddSingleton<ConfigManager>();

            services.AddSingleton<ILogger, Logger>();
            services.AddSingleton<IMessenger, WeakReferenceMessenger>();
            services.AddSingleton<IEventSimulator, EventSimulator>();

            services.AddSingleton<IWebDav, WebDavClient>();
            services.AddSingleton<IHttp, Http>();
            services.AddSingleton<ServiceManager>();
            services.AddSingleton<HotkeyManager>();
            services.AddTransient<GithubUpdater>();
            services.AddTransient<System.Timers.Timer>();

            services.AddTransient<AppInstance>();
        }

        public static void ConfigurateViewModels(IServiceCollection services)
        {
            services.AddTransient<SyncSettingViewModel>();
            services.AddTransient<SystemSettingViewModel>();
            services.AddTransient<AboutViewModel>();
            services.AddTransient<CliboardAssistantViewModel>();
            services.AddTransient<NextCloudLogInViewModel>();
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
            services.AddSingleton<IService, DownloadService>();
        }

        private async void PrepareRemoteWorkingFolder()
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

        private void PrepareWorkingFolder(ConfigManager configManager)
        {
            DeletePreviousTempFile();
            PlannedTask(configManager);

            var timer = Services.GetRequiredService<System.Timers.Timer>();
            timer.Interval = TimeSpan.FromDays(1).TotalMilliseconds;
            timer.Elapsed += (_, _) => PlannedTask(configManager);
            timer.Start();
        }

        private static void PlannedTask(ConfigManager configManager)
        {
            try
            {
                var config = configManager.GetConfig<ProgramConfig>();
                var tempFolders = new DirectoryInfo(Env.AppDataFileFolder).EnumerateDirectories("????????");
                foreach (var dirs in tempFolders)
                {
                    var createTime = DateTime.ParseExact(dirs.Name, "yyyyMMdd", CultureInfo.InvariantCulture);
                    if ((DateTime.Today - createTime) > TimeSpan.FromDays(1))
                    {
                        dirs.Delete(true);
                    }
                }

                var logFolder = new DirectoryInfo(Env.LogFolder);
                if (logFolder.Exists && config.LogRemainDays != 0)
                {
                    var logFiles = logFolder.EnumerateFiles("????????.txt");
                    var dumpFiles = logFolder.EnumerateFiles("????-??-?? ??-??-??.dmp");
                    DeleteOutDateFile(logFiles, "yyyyMMdd", TimeSpan.FromDays(config.LogRemainDays));
                    DeleteOutDateFile(dumpFiles, "yyyy-MM-dd HH-mm-ss", TimeSpan.FromDays(config.LogRemainDays));
                }
            }
            catch { }
        }

        private static void DeleteOutDateFile(IEnumerable<FileSystemInfo> files, string format, TimeSpan time)
        {
            foreach (var file in files)
            {
                var createTime = DateTime.ParseExact(
                    Path.GetFileNameWithoutExtension(file.Name),
                    format,
                    CultureInfo.InvariantCulture);
                if ((DateTime.Today - createTime) > time)
                {
                    file.Delete();
                }
            }
        }

        // 删除历史版本的临时文件，在未来版本中删除
        private static void DeletePreviousTempFile()
        {
            try
            {
                var files = new DirectoryInfo(Env.AppDataFileFolder).EnumerateFiles();
                foreach (var file in files)
                {
                    file.Delete();
                }

                var oldImageFolder = Path.Combine(Env.AppDataFileFolder, "temp images");
                if (Directory.Exists(oldImageFolder))
                {
                    Directory.Delete(oldImageFolder, true);
                }
            }
            catch { }
        }
    }
}