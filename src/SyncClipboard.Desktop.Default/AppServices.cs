using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Desktop.Default.Views;

namespace SyncClipboard.Desktop.Default;

public class AppServices
{
    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        Desktop.AppServices.ConfigDesktopCommonService(services);

        services.AddSingleton<ITrayIcon, TrayIconImpl>();
        return services;
    }
}
