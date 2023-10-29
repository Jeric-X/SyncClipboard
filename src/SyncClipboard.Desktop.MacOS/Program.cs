using System;
using Avalonia;
using SyncClipboard.Desktop;
using SyncClipboard.Desktop.MacOS;
using SyncClipboard.Core.Utilities;
using AppKit;

namespace SyncClipboard.Desktop.MacOS;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {    
        using var mutex = AppInstance.EnsureSingleInstance();
        if (mutex is null)
        {
            return;
        }
        NSApplication.Init();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>(() => new App(AppServices.ConfigureServices()))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
