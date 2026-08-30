using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class HotkeyBlacklistPage : UserControl
{
    private readonly HotkeyBlacklistViewModel _viewModel;

    public HotkeyBlacklistPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<HotkeyBlacklistViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
        _viewModel.WindowCaptured += OnWindowCaptured;
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
    }

    private async void AddItemClick(object? _, RoutedEventArgs __)
    {
        var dialog = new WindowInfoEditDialog();
        if (await dialog.ShowAsync(App.Current.MainWindow) == ContentDialogResult.Primary)
        {
            _viewModel.AddItem(dialog.GetWindowInfo());
        }
    }

    private async void EditItemClick(object? sender, RoutedEventArgs _)
    {
        if (sender is not Button { DataContext: EditableWindowInfo item }) return;
        var dialog = new WindowInfoEditDialog();
        dialog.SetWindowInfo(item.ToWindowInfo());
        if (await dialog.ShowAsync(App.Current.MainWindow) == ContentDialogResult.Primary)
        {
            _viewModel.UpdateItem(item, dialog.GetWindowInfo());
        }
    }

    private void DeleteItemClick(object? sender, RoutedEventArgs _)
    {
        if (sender is Button { DataContext: EditableWindowInfo item }) _viewModel.RemoveItem(item);
    }

    private void CaptureClick(object? _, RoutedEventArgs __)
    {
        _viewModel.StartCaptureCommand.Execute(null);
    }

    private void StopCaptureClick(object? _, RoutedEventArgs __)
    {
        _viewModel.StopCaptureCommand.Execute(null);
    }

    private void OnWindowCaptured(WindowInfo info)
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            App.Current.MainWindow.Show();
            var dialog = new WindowInfoEditDialog();
            dialog.SetWindowInfo(info);
            if (await dialog.ShowAsync(App.Current.MainWindow) == ContentDialogResult.Primary)
            {
                _viewModel.AddItem(dialog.GetWindowInfo());
            }
        });
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        _viewModel.StopCapture();
        _viewModel.WindowCaptured -= OnWindowCaptured;
    }
}
