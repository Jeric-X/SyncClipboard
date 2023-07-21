using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core;
using SyncClipboard.WinUI3.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using SyncClipboard.WinUI3.Clipboard;
using SyncClipboard.Core.Clipboard;
using Microsoft.UI.Xaml;

namespace SyncClipboard.WinUI3;

public class AppServices
{
    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        ProgramWorkflow.ConfigCommonService(services);

        services.AddSingleton<IMainWindow, SettingWindow>();
        services.AddSingleton<IClipboardChangingListener>(
            (sp) => new ClipboardListener(
                (Window)sp.GetRequiredService<IClipboardFactory>(),
                sp.GetRequiredService<IClipboardFactory>()
            )
        );
        services.AddSingleton<TrayIcon>();
        services.AddSingleton<ITrayIcon, TrayIconImpl>();
        services.AddSingleton<IContextMenu, TrayIconContextMenu>();

        return services;
    }
}
