using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Notification;
using SyncClipboard.WinUI3.ClipboardWinUI;
using SyncClipboard.WinUI3.Views;

namespace SyncClipboard.WinUI3;

public class AppServices
{
    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        ProgramWorkflow.ConfigCommonService(services);
        ProgramWorkflow.ConfigurateViewModels(services);
        ProgramWorkflow.ConfigurateUserService(services);

        services.AddTransient<IAppConfig, AppConfig>();

        services.AddSingleton<IMainWindow, MainWindow>();
        services.AddSingleton<IClipboardChangingListener, ClipboardListener>();
        services.AddSingleton<ClipboardFactory>();
        services.AddSingleton<IClipboardFactory>(sp => sp.GetRequiredService<ClipboardFactory>());
        services.AddSingleton<IProfileDtoHelper>(sp => sp.GetRequiredService<ClipboardFactory>());
        services.AddSingleton<TrayIcon>(sp => ((MainWindow)sp.GetRequiredService<IMainWindow>()).TrayIcon);
        services.AddSingleton<ITrayIcon, TrayIconImpl>();
        services.AddSingleton<IContextMenu, TrayIconContextMenu>();
        services.AddSingleton<INotification, NotificationManager>();

        services.AddTransient<IClipboardSetter<TextProfile>, TextClipboardSetter>();
        services.AddTransient<IClipboardSetter<FileProfile>, FileClipboardSetter>();
        services.AddTransient<IClipboardSetter<ImageProfile>, ImageClipboardSetter>();

        return services;
    }
}
