using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;

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
        }
    }
}