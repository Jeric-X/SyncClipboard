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
        services.AddSingleton<IService, ServerService>();
        services.AddSingleton<IService, UploadService>();
        services.AddSingleton<IService, DownloadService>();
    }

    public static void ConfigDesktopCommonService(IServiceCollection services)
    {
        ProgramWorkflow.ConfigCommonService(services);
        ProgramWorkflow.ConfigurateViewModels(services);
        ConfigurateUserService(services);

        services.AddTransient<IAppConfig, AppConfig>();

        services.AddSingleton<IContextMenu, TrayIconContextMenu>();
        services.AddSingleton<ITrayIcon, TrayIconImpl>();

        services.AddSingleton<IClipboardFactory, ClipboardFactory>();
        services.AddSingleton<IClipboardChangingListener, ClipboardListener>();
        services.AddTransient<IClipboardSetter<TextProfile>, TextClipboardSetter>();
        services.AddTransient<IClipboardSetter<FileProfile>, FileClipboardSetter>();
        services.AddTransient<IClipboardSetter<ImageProfile>, ImageClipboardSetter>();

        services.AddTransient<IFontManager, FontManager>();

        services.AddSingleton<INotification, Notification>();
    }

    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        ConfigDesktopCommonService(services);

        services.AddSingleton<IMainWindow, MainWindow>();
        return services;
    }
}
