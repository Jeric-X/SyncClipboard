using Microsoft.Extensions.DependencyInjection;
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

        services.AddSingleton<INativeHotkeyRegistry, CarbonHotkeyRegistry>();
        services.AddSingleton<ICaretPositionProvider, CaretPositionProvider>();
        services.AddSingleton<IForegroundWindowInfoProvider, ForegroundWindowInfoProvider>();
        services.AddSingleton<IMainWindow, MainWindow>();
        services.AddKeyedSingleton<IWindow, HistoryWindow>("HistoryWindow");
        services.AddSingleton<ITrayIcon, TrayIconImpl>();
        return services;
    }
}
