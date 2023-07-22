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
using SyncClipboard.Core.Clipboard;
using Microsoft.UI.Xaml;
using SyncClipboard.WinUI3.ClipboardWinUI;

namespace SyncClipboard.WinUI3;

public class AppServices
{
    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        ProgramWorkflow.ConfigCommonService(services);

        services.AddSingleton<IMainWindow, SettingWindow>();
        services.AddSingleton<IClipboardChangingListener, ClipboardListener>();
        services.AddSingleton<IClipboardFactory, ClipboardFactory>();
        services.AddSingleton<TrayIcon>();
        services.AddSingleton<ITrayIcon, TrayIconImpl>();
        services.AddSingleton<IContextMenu, TrayIconContextMenu>();

        return services;
    }
}
