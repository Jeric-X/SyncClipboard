using AppKit;
using Avalonia;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Utilities;
using System;
using System.IO;

namespace SyncClipboard.Desktop.MacOS;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (AppInstance.EnsureSingleInstance(args) is false)
        {
            return;
        }
        NSApplication.Init();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            var path = Path.Combine(Env.LogFolder, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss}.dmp");
            File.WriteAllText(path + ".txt", $"UnhandledException {e.GetType()} {e.Message} \n{e.StackTrace}");
            App.Current?.Logger?.Write($"UnhandledException {e.GetType()} {e.Message} \n {e.StackTrace}");
            // The event loop has already failed; cleanup must not wait indefinitely for dispatcher work.
            try
            {
                App.Current?.AppCore?.StopAsync().Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception shutdownException)
            {
                System.Diagnostics.Trace.WriteLine($"Shutdown after an unhandled exception failed: {shutdownException}");
            }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure(() => new App(AppServices.ConfigureServices()))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .With(new MacOSPlatformOptions { ShowInDock = false });
    }
}
