using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Core;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Desktop.ClipboardAva;
using SyncClipboard.Desktop.Utilities;
using SyncClipboard.Desktop.Views;

namespace SyncClipboard.Desktop;

public class AppServices
{
    private static void ConfigurateUserService(IServiceCollection services)
    {
        //services.AddSingleton<IService, EasyCopyImageSerivce>();
        //services.AddSingleton<IService, ConvertService>();
        //services.AddSingleton<IService, ServerService>();
        services.AddSingleton<IService, UploadService>();
        //services.AddSingleton<IService, DownloadService>();
    }

    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        ProgramWorkflow.ConfigCommonService(services);
        ProgramWorkflow.ConfigurateViewModels(services);
        ConfigurateUserService(services);

        services.AddSingleton<IContextMenu, TrayIconContextMenu>();
        services.AddSingleton<IMainWindow, MainWindow>();
        services.AddSingleton<ITrayIcon, TrayIconImpl>();

        services.AddSingleton<IClipboardFactory, ClipboardFactory>();
        services.AddSingleton<INotification, Notification>();

        return services;
    }
}
