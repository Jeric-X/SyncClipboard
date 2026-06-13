using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class ClipboardOwnerFilterSettingPage : UserControl
{
    private readonly ClipboardOwnerFilterSettingViewModel _viewModel;
    public ClipboardOwnerFilterSettingPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<ClipboardOwnerFilterSettingViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        _viewModel.OnClipboardOwnerCaptured += OnClipboardOwnerCaptured;
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        _viewModel.StopListening();
        _viewModel.OnClipboardOwnerCaptured -= OnClipboardOwnerCaptured;
        App.Current.MainWindow.EnableScrollViewer();
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        App.Current.MainWindow.DispableScrollViewer();
    }

    private async void AddItemClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new WindowInfoEditDialog();
        var result = await dialog.ShowAsync(App.Current.MainWindow);
        if (result == ContentDialogResult.Primary)
        {
            _viewModel.AddItem(dialog.GetWindowInfo());
        }
    }

    private async void EditItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is EditableWindowInfo item)
        {
            var dialog = new WindowInfoEditDialog();
            dialog.SetWindowInfo(item.ToWindowInfo());
            var result = await dialog.ShowAsync(App.Current.MainWindow);
            if (result == ContentDialogResult.Primary)
            {
                _viewModel.UpdateItem(item, dialog.GetWindowInfo());
            }
        }
    }

    private void DeleteItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is EditableWindowInfo item)
        {
            _viewModel.RemoveItem(item);
        }
    }

    private void CaptureClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.StartListening();
    }

    private async void OnClipboardOwnerCaptured(ForegroundWindowInfo info)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            App.Current.MainWindow.Show();

            var dialog = new WindowInfoEditDialog();
            dialog.SetWindowInfo(info);
            var result = await dialog.ShowAsync(App.Current.MainWindow);
            if (result == ContentDialogResult.Primary)
            {
                _viewModel.AddItem(dialog.GetWindowInfo());
            }
        });
    }
}
