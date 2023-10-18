using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Desktop.Views;

namespace SyncClipboard.Desktop;

public class AppServices
{
    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        ProgramWorkflow.ConfigCommonService(services);
        ProgramWorkflow.ConfigurateViewModels(services);

        services.AddSingleton<IContextMenu, TrayIconContextMenu>();
        services.AddSingleton<IMainWindow, MainWindow>();
        services.AddSingleton<ITrayIcon, TrayIconImpl>();

        return services;
    }
}
