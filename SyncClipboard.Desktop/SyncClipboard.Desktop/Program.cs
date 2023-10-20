using Avalonia;
using SyncClipboard.Core.Utilities;
using System;
using System.Threading;

namespace SyncClipboard.Desktop.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using var _ = new Mutex(false, Env.AppId, out bool createdNew);
        if (!createdNew)
        {
            AppInstance.ActiveOtherInstance(Env.AppId).Wait();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

}
