using Avalonia.Controls;
using System;
using SyncClipboard.Desktop;
using AppKit;
using Avalonia;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.MacOS.Views;

public class MainWindow : SyncClipboard.Desktop.Views.MainWindow
{
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        NSApplication.SharedApplication.Hide(NSApplication.SharedApplication);
        e.Cancel = true;
    }

    public override async void Init(bool hide)
    {
        if (hide is false)
        {
            base.ShowMainWindow();
            return;
        }

        _created = true;
        var transparencyLevelHint = TransparencyLevelHint;
        var background = Background;
        var extendClientAreaTitleBarHeightHint = ExtendClientAreaTitleBarHeightHint;
        var opacity = Opacity;
        var extendClientAreaChromeHints = ExtendClientAreaChromeHints;

        TransparencyLevelHint = new WindowTransparencyLevel[] { WindowTransparencyLevel.Transparent };
        Background = null;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = 0;
        this.Opacity = 0;
        ShowMainWindow();
        NSApplication.SharedApplication.Hide(NSApplication.SharedApplication);

        await Task.Delay(500).ConfigureAwait(true);
        TransparencyLevelHint = transparencyLevelHint;
        Background = background;
        ExtendClientAreaTitleBarHeightHint = extendClientAreaTitleBarHeightHint;
        this.Opacity = opacity;
        ExtendClientAreaChromeHints = extendClientAreaChromeHints;
    }

    private bool _created = false;
    protected override void ShowMainWindow()
    {
        if (_created)
        {
            NSApplication.SharedApplication.Unhide(NSApplication.SharedApplication);
        }
        else
        {
            base.ShowMainWindow();
            _created = true;
        }
    }
}
