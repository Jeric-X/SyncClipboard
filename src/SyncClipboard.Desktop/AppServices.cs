using System;
using Microsoft.Extensions.DependencyInjection;
using SharpHook;
using SyncClipboard.Core;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Desktop.ClipboardAva;
using SyncClipboard.Desktop.ClipboardAva.ClipboardReader;
using SyncClipboard.Desktop.Utilities;
using SyncClipboard.Desktop.Views;

namespace SyncClipboard.Desktop;

public class AppServices
{
    public static void ConfigDesktopCommonService(IServiceCollection services)
    {
        AppCore.ConfigCommonService(services);
        AppCore.ConfigurateViewModels(services);
        AppCore.ConfigurateUserService(services);

        services.AddTransient<IAppConfig, AppConfig>();

        services.AddSingleton<IMainWindowDialog, Services.AvaloniaDialog>();
        services.AddKeyedSingleton<IMainWindowDialog>("HistoryWindow", (sp, key) =>
        {
            var historyWindow = sp.GetRequiredKeyedService<IWindow>("HistoryWindow") as HistoryWindow;
            return new Services.AvaloniaDialog(historyWindow!);
        });
        services.AddSingleton<IContextMenu, TrayIconContextMenu>();
        services.AddSingleton<MultiSourceClipboardReader>();
        services.AddSingleton<IClipboardReader, AvaloniaClipboardReader>();

        services.AddSingleton<IClipboardFactory, ClipboardFactory>();
        services.AddSingleton<ClipboardListener>();
        services.AddSingleton<IClipboardChangingListener>(sp => sp.GetRequiredService<ClipboardListener>());
        services.AddSingleton<IClipboardMoniter>(sp => sp.GetRequiredService<ClipboardListener>());
        services.AddTransient<IClipboardSetter<TextProfile>, TextClipboardSetter>();
        services.AddTransient<IClipboardSetter<FileProfile>, FileClipboardSetter>();
        services.AddTransient<IClipboardSetter<ImageProfile>, ImageClipboardSetter>();
        services.AddTransient<IClipboardSetter<GroupProfile>, FileClipboardSetter>();

        services.AddSingleton<IGlobalHook>((sp) => new SimpleGlobalHook(true));

        services.AddTransient<IFontManager, FontManager>();
        services.AddTransient<IThreadDispatcher, ThreadDispatcher>();

        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IClipboardReader, XClipReader>();
            services.AddSingleton<IClipboardReader, WlClipboardReader>();
        }

        if (!OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IMainWindow, MainWindow>();
            services.AddSingleton<INativeHotkeyRegistry, SharpHookHotkeyRegistry>();
            services.AddKeyedSingleton<IWindow, HistoryWindow>("HistoryWindow");
        }
    }

    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        ConfigDesktopCommonService(services);

        return services;
    }
}
