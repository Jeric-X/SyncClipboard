using AppKit;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using SyncClipboard.Desktop.MacOS.Utilities;
using System;

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
        if (e.Kind == ActivationKind.Reopen)
        {
            if (ApplicationActivationHelper.ActiveWindows.Count == 0)
            {
                ShowMainWindow();
            }
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        this.RemoveWindow();
        base.OnClosing(e);
    }

    protected override void ShowMainWindow()
    {
        this.AddWindow();
        this.FocusMenuBar();
        base.ShowMainWindow();
    }

    protected override void MinimizeWindow()
    {
        NSApplication.SharedApplication.MainWindow.Miniaturize(NSApplication.SharedApplication);
    }
}
