using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class FileSyncFilterSettingPage : Page
{
    private readonly FileSyncFilterSettingViewModel _viewModel;

    public FileSyncFilterSettingPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<FileSyncFilterSettingViewModel>();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ((MainWindow)App.Current.Services.GetRequiredService<IMainWindow>()).EnableScrollViewer();
        base.OnNavigatedFrom(e);
    }

    protected override void OnNavigatedTo(NavigationEventArgs _)
    {
        ((MainWindow)App.Current.Services.GetRequiredService<IMainWindow>()).DispableScrollViewer();
    }

    private async void AddItemClick(object _, RoutedEventArgs __)
    {
        var editor = FileSyncFilterSettingViewModel.CreateRuleEditor();
        var dialog = new FileFilterRuleEditDialog(editor)
        {
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _viewModel.AddItem(editor.ToRule());
        }
    }

    private async void EditItemClick(object sender, RoutedEventArgs _)
    {
        if (sender is not Button { DataContext: EditableFileFilterRule item })
        {
            return;
        }

        var editor = FileSyncFilterSettingViewModel.CreateRuleEditor(item.ToRule());
        var dialog = new FileFilterRuleEditDialog(editor)
        {
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _viewModel.UpdateItem(item, editor.ToRule());
        }
    }

    private void DeleteItemClick(object sender, RoutedEventArgs _)
    {
        if (sender is Button { DataContext: EditableFileFilterRule item })
        {
            _viewModel.RemoveItem(item);
        }
    }
}
