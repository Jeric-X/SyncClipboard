using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Media.Animation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using System;
using SyncClipboard.Desktop;
using AppKit;
using System.Reflection;
using Avalonia;
using ObjCRuntime;

namespace SyncClipboard.Desktop.MacOS.Views;

public class MainWindow : SyncClipboard.Desktop.Views.MainWindow
{
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var window = NSApplication.SharedApplication.MainWindow;
        window.Miniaturize(window);
        e.Cancel = true;
    }

    // protected override void ShowMainWindow()
    // {
    //     var window = NSApplication.SharedApplication.MainWindow;
    //     window.Deminiaturize(window);
    // }
}
