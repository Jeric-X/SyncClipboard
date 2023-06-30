using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Options;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core
{
    public class ProgramWorkflow
    {
        public IServiceProvider Services { get; }

        public ProgramWorkflow(IServiceProvider serviceProvider)
        {
            Services = serviceProvider;
        }

        public void Run()
        {
            var trayIcon = Services.GetRequiredService<ITrayIcon>();
            trayIcon.MainWindowWakedUp += Services.GetRequiredService<IMainWindow>().Show;
            trayIcon.Create();

            var userConfig = Services.GetRequiredService<UserConfig>();
            userConfig.AddMenuItems();

            var webdav = Services.GetRequiredService<IWebDav>();
            webdav.TestAlive();
        }

        public static void ConfigCommonService(ServiceCollection services)
        {
            services.AddSingleton<UserConfig>();
            services.Configure<UserConfigOption>(x => x.Path = Env.UserConfigFile);

            services.AddSingleton<ILogger, Logger>();
            services.Configure<LoggerOption>(x => x.Path = Env.LogFolder);

            services.AddSingleton<IWebDav, WebDavClient>();
            services.AddSingleton<IHttp, Http>();
        }
    }
}