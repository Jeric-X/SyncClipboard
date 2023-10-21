using Avalonia.Controls;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Desktop.Views;

public partial class MainWindow : Window, IMainWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        this.Hide();
        e.Cancel = true;
    }
}
