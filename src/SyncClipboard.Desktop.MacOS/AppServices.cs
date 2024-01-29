using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Desktop.MacOS.Utilities;
using SyncClipboard.Desktop.MacOS.Views;

namespace SyncClipboard.Desktop.MacOS;

public class AppServices
{
    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        Desktop.AppServices.ConfigDesktopCommonService(services);

        services.AddSingleton<IMainWindow, MainWindow>();
        services.AddSingleton<INotification, Notification>();
        return services;
    }
}
