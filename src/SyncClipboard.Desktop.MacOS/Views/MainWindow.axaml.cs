using AppKit;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using System;
using System.Linq;

namespace SyncClipboard.Desktop.MacOS.Views;

public class MainWindow : Desktop.Views.MainWindow
{
    public MainWindow()
    {
        var lifetime = App.Current.TryGetFeature<IActivatableLifetime>()
            ?? throw new NotSupportedException("Mac AppLifetime wrong.");
        lifetime.Activated += Lifitime_Activated;
    }

    private void Lifitime_Activated(object? sender, ActivatedEventArgs e)
    {
        ShowMainWindow();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Accessory;
        base.OnClosing(e);
    }

    protected override void ShowMainWindow()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Regular;

        // 这是一个workaround，切换NSApplicationActivationPolicy后系统菜单无法被点击，需要先Focus到其他App
        NSRunningApplication.GetRunningApplications("com.apple.dock")
            .FirstOrDefault()?.Activate(NSApplicationActivationOptions.ActivateIgnoringOtherWindows);

        base.ShowMainWindow();
    }

    protected override void MinimizeWindow()
    {
        NSApplication.SharedApplication.MainWindow.Miniaturize(NSApplication.SharedApplication);
    }
}
