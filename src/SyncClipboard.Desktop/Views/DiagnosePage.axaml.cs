using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Desktop.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class DiagnosePage : UserControl
{
    public DiagnosePage()
    {
        var viewModel = App.Current.Services.GetRequiredService<DiagnoseViewModel>();
        DataContext = viewModel;
        viewModel.RefreshCommand.Execute(null);
        InitializeComponent();
    }

    private void SettingsExpanderItem_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var para = (sender as FASettingsExpanderItem)?.Content;
        var page = new PageDefinition("DiagnoseDetail", "DiagnoseDetail");

        App.Current.MainWindow.NavigateToNextLevel(page, para);
    }
}
