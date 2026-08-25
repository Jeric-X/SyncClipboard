using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class FileSyncFilterSettingPage : UserControl
{
    private readonly FileSyncFilterSettingViewModel _viewModel;
    public FileSyncFilterSettingPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<FileSyncFilterSettingViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        App.Current.MainWindow.EnableScrollViewer();
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        App.Current.MainWindow.DispableScrollViewer();
    }

    private async void AddItemClick(object? sender, RoutedEventArgs e)
    {
        var editor = FileSyncFilterSettingViewModel.CreateRuleEditor();
        var dialog = new FileFilterRuleEditDialog(editor);
        var result = await dialog.ShowAsync(App.Current.MainWindow);
        if (result == ContentDialogResult.Primary)
        {
            _viewModel.AddItem(editor.ToRule());
        }
    }

    private async void EditItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: EditableFileFilterRule item })
        {
            return;
        }

        var editor = FileSyncFilterSettingViewModel.CreateRuleEditor(item.ToRule());
        var dialog = new FileFilterRuleEditDialog(editor);
        var result = await dialog.ShowAsync(App.Current.MainWindow);
        if (result == ContentDialogResult.Primary)
        {
            _viewModel.UpdateItem(item, editor.ToRule());
        }
    }

    private void DeleteItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: EditableFileFilterRule item })
        {
            _viewModel.RemoveItem(item);
        }
    }
}
