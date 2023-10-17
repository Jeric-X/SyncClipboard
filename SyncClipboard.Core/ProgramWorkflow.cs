using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Options;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Web;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Core
{
    public class ProgramWorkflow
    {
        public IServiceProvider Services { get; }
        private ServiceManager? ServiceManager { get; set; }

        public ProgramWorkflow(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
        }

        private static void InitLanguage(ConfigManager configManager)
        {
            var langTag = configManager.GetConfig<ProgramConfig>(ConfigKey.Program)?.Language;
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
            contextMenu.AddMenuItem(new MenuItem(Strings.Settings, Services.GetRequiredService<IMainWindow>().Show), "Top Group");

            var trayIcon = Services.GetRequiredService<ITrayIcon>();
            trayIcon.MainWindowWakedUp += Services.GetRequiredService<IMainWindow>().Show;

            configManager.AddMenuItems();

            var webdav = Services.GetRequiredService<IWebDav>();
            webdav.TestAlive().ContinueWith(async res =>
            {
                if (await res)
                {
                    await webdav.CreateDirectory(Env.RemoteFileFolder);
                }
            });

            PrepareWorkingFolder(configManager);
            CheckUpdate();
            ServiceManager = Services.GetRequiredService<ServiceManager>();
            ServiceManager.StartUpAllService();
            trayIcon.Create();
        }

        private async void CheckUpdate()
        {
            var configManager = Services.GetRequiredService<ConfigManager>();
            var updateChecker = Services.GetRequiredService<UpdateChecker>();
            var notificationManager = Services.GetRequiredService<INotification>();

            bool checkOnStartup = configManager.GetConfig<ProgramConfig>(ConfigKey.Program)?.CheckUpdateOnStartUp ?? false;
            if (checkOnStartup)
            {
                try
                {
                    var (needUpdate, newVersion) = await updateChecker.Check();
                    if (needUpdate)
                    {
                        notificationManager.SendText(
                            Strings.FoundNewVersion,
                            $"v{Env.VERSION} -> {newVersion}",
                            new Button(Strings.OpenDownloadPage, () => Sys.OpenWithDefaultApp(UpdateChecker.ReleaseUrl))
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

        public static void ConfigCommonService(ServiceCollection services)
        {
            services.AddSingleton((serviceProvider) => serviceProvider);
            services.AddTransient<IAppConfig, AppConfig>();
            services.Configure<UserConfigOption>(x => x.Path = Env.UserConfigFile);
            services.AddSingleton<ConfigManager>();

            services.AddSingleton<ILogger, Logger>();
            services.Configure<LoggerOption>(x => x.Path = Env.LogFolder);

            services.AddSingleton<IWebDav, WebDavClient>();
            services.AddSingleton<IHttp, Http>();
            services.AddSingleton<ServiceManager>();
            services.AddTransient<UpdateChecker>();

            ConfigurateUserService(services);
        }

        public static void ConfigurateViewModels(IServiceCollection services)
        {
            services.AddTransient<SyncSettingViewModel>();
            services.AddTransient<SystemSettingViewModel>();
            services.AddTransient<AboutViewModel>();
            services.AddTransient<CliboardAssistantViewModel>();
            services.AddTransient<NextCloudLogInViewModel>();
            services.AddSingleton<ServiceStatusViewModel>();
            services.AddSingleton<SettingWindowViewModel>();
        }

        private static void ConfigurateUserService(IServiceCollection services)
        {
            services.AddSingleton<IService, EasyCopyImageSerivce>();
            services.AddSingleton<IService, ConvertService>();
            services.AddSingleton<IService, ServerService>();
            services.AddSingleton<IService, UploadService>();
            services.AddSingleton<IService, DownloadService>();
        }

        private static void PrepareWorkingFolder(ConfigManager configManager)
        {
            var config = configManager.GetConfig<ProgramConfig>(ConfigKey.Program) ?? new();
            if (Directory.Exists(Env.TemplateFileFolder))
            {
                if (config.DeleteTempFilesOnStartUp)
                {
                    Directory.Delete(Env.TemplateFileFolder, true);
                    Directory.CreateDirectory(Env.TemplateFileFolder);
                }
            }
            else
            {
                Directory.CreateDirectory(Env.TemplateFileFolder);
            }

            var logFolder = new DirectoryInfo(Env.LogFolder);
            if (logFolder.Exists && config.LogRemainDays != 0)
            {
                var today = DateTime.Today;
                foreach (var logFile in logFolder.EnumerateFileSystemInfos("????????.txt"))
                {
                    var createTime = logFile.CreationTime.Date;
                    if ((today - createTime) > TimeSpan.FromDays(config.LogRemainDays))
                    {
                        logFile.Delete();
                    }
                }
            }
        }
    }
}