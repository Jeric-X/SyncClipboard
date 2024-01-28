using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Desktop.Default.Utilities;

namespace SyncClipboard.Desktop.Default;

public class AppServices
{
    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        Desktop.AppServices.ConfigDesktopCommonService(services);

        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<INotification, Notification>();
        }
        return services;
    }
}
