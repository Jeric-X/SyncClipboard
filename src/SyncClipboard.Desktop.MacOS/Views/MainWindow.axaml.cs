using AppKit;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System;

namespace SyncClipboard.Desktop.MacOS.Views;

public class MainWindow : Desktop.Views.MainWindow
{
    public MainWindow()
    {
        if (App.Current.ApplicationLifetime is not IActivatableApplicationLifetime lifetime)
        {
            throw new NotSupportedException("Mac AppLifetime wrong.");
        }
        lifetime.Activated += Lifitime_Activated;
    }

    private void Lifitime_Activated(object? sender, ActivatedEventArgs e)
    {
        ShowMainWindow();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Hide();
        e.Cancel = true;
    }

    protected override void MinimizeWindow()
    {
        NSApplication.SharedApplication.MainWindow.Miniaturize(NSApplication.SharedApplication);
    }
}
