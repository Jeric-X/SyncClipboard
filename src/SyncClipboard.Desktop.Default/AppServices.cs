using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Desktop.Default.Utilities;
using SyncClipboard.Desktop.Default.Views;

namespace SyncClipboard.Desktop.Default;

public class AppServices
{
    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        Desktop.AppServices.ConfigDesktopCommonService(services);

        services.AddSingleton<ITrayIcon, TrayIconImpl>();

        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<INotification, Notification>();
        }
        return services;
    }
}
