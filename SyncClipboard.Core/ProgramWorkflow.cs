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
            var trayIcon = Services.GetService<ITrayIcon>();
            ArgumentNullException.ThrowIfNull(trayIcon);
            trayIcon.Create();

            var userConfig = Services.GetService<UserConfig>();
            ArgumentNullException.ThrowIfNull(userConfig);
            userConfig.AddMenuItems();
        }

        public static void ConfigCommonService(ServiceCollection services)
        {
            services.AddSingleton<UserConfig>();
            services.Configure<UserConfigOption>(x => x.Path = Env.UserConfigFile);
            services.AddSingleton<ILogger, Logger>();
            services.Configure<LoggerOption>(x => x.Path = Env.LogFolder);
        }
    }
}