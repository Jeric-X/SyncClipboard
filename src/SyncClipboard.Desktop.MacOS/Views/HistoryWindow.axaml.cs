using Avalonia.Controls;
using SyncClipboard.Desktop.MacOS.Utilities;

namespace SyncClipboard.Desktop.MacOS.Views;

public class HistoryWindow : Desktop.Views.HistoryWindow
{
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        this.RemoveWindow();
        base.OnClosing(e);
    }

    protected override void FocusOnScreen()
    {
        this.AddWindow();
        // this.FocusMenuBar();
        base.FocusOnScreen();
    }
}
