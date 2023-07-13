using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core;
using SyncClipboard.WinUI3.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3;

public class AppServices
{
    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        ProgramWorkflow.ConfigCommonService(services);

        services.AddSingleton<IMainWindow, SettingWindow>();
        services.AddSingleton<ITrayIcon, TrayIcon>();
        services.AddSingleton<IContextMenu>((sp) => new TrayIconContextMenu((TrayIcon)sp.GetRequiredService<ITrayIcon>()));

        return services;
    }
}
