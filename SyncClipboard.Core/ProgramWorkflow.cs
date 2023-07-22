using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Options;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Notification;
using SyncClipboard.Core.Utilities.Web;

namespace SyncClipboard.Core
{
    public class ProgramWorkflow
    {
        public IServiceProvider Services { get; }
        public ServiceManager ServiceManager { get; }

        public ProgramWorkflow(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
            ServiceManager = Services.GetRequiredService<ServiceManager>();
        }

        public void Run()
        {
            var contextMenu = Services.GetRequiredService<IContextMenu>();
            contextMenu.AddMenuItem(new MenuItem("设置", Services.GetRequiredService<IMainWindow>().Show));

            var trayIcon = Services.GetRequiredService<ITrayIcon>();
            trayIcon.MainWindowWakedUp += Services.GetRequiredService<IMainWindow>().Show;
            trayIcon.Create();

            var userConfig = Services.GetRequiredService<UserConfig>();
            userConfig.AddMenuItems();

            var webdav = Services.GetRequiredService<IWebDav>();
            webdav.TestAlive();

            PrepareWorkingFolder(userConfig);
            ServiceManager.StartUpAllService();
        }

        public void Stop()
        {
            ServiceManager.StopAllService();
        }

        public static void ConfigCommonService(ServiceCollection services)
        {
            services.AddSingleton((serviceProvider) => serviceProvider);
            services.AddSingleton<UserConfig>();
            services.AddTransient<IAppConfig, AppConfig>();
            services.Configure<UserConfigOption>(x => x.Path = Env.UserConfigFile);

            services.AddSingleton<ILogger, Logger>();
            services.Configure<LoggerOption>(x => x.Path = Env.LogFolder);

            services.AddSingleton<IWebDav, WebDavClient>();
            services.AddSingleton<IHttp, Http>();
            services.AddSingleton<NotificationManager>();
            services.AddSingleton<ServiceManager>();

            ConfigurateUserService(services);
        }

        private static void ConfigurateUserService(IServiceCollection services)
        {
            services.AddSingleton<IService, CommandService>();
            services.AddSingleton<IService, EasyCopyImageSerivce>();
            services.AddSingleton<IService, ConvertService>();
            services.AddSingleton<IService, ServerService>();
            services.AddSingleton<IService, UploadService>();
            services.AddSingleton<IService, DownloadService>();
        }

        private static void PrepareWorkingFolder(UserConfig userConfig)
        {
            if (Directory.Exists(Env.TemplateFileFolder))
            {
                if (userConfig.Config.Program.DeleteTempFilesOnStartUp)
                {
                    Directory.Delete(Env.TemplateFileFolder);
                    Directory.CreateDirectory(Env.TemplateFileFolder);
                }
            }
            else
            {
                Directory.CreateDirectory(Env.TemplateFileFolder);
            }

            var logFolder = new DirectoryInfo(Env.LogFolder);
            if (logFolder.Exists && userConfig.Config.Program.LogRemainDays != 0)
            {
                var today = DateTime.Today;
                foreach (var logFile in logFolder.EnumerateFileSystemInfos("????????.txt"))
                {
                    var createTime = logFile.CreationTime.Date;
                    if ((today - createTime) > TimeSpan.FromDays(userConfig.Config.Program.LogRemainDays))
                    {
                        logFile.Delete();
                    }
                }
            }
        }
    }
}