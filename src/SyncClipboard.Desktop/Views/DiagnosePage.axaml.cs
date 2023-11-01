using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Desktop.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class DiagnosePage : UserControl
{
    public DiagnosePage()
    {
        DataContext = new DiagnoseViewModel();
        InitializeComponent();
    }

    private void SettingsExpanderItem_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var para = (sender as SettingsExpanderItem)?.Content;
        var page = new PageDefinition("DiagnoseDetail", "DiagnoseDetail");

        App.Current.MainWindow.NavigateToNextLevel(page, para);
    }
}
