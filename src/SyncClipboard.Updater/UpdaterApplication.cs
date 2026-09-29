using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace SyncClipboard.Updater;

internal sealed class UpdaterApplication : Application
{
    public static bool SmokeTest { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new UpdateWindow(SmokeTest, desktop);
        base.OnFrameworkInitializationCompleted();
    }
}
