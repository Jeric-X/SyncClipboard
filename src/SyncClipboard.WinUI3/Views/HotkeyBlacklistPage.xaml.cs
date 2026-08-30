using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HotkeyBlacklistPage : Page
{
    private readonly HotkeyBlacklistViewModel _viewModel;

    public HotkeyBlacklistPage()
    {
        InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<HotkeyBlacklistViewModel>();
        _viewModel.WindowCaptured += OnWindowCaptured;
    }

    private async void AddItemClick(object _, RoutedEventArgs __)
    {
        var dialog = new WindowInfoEditDialog { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _viewModel.AddItem(dialog.GetWindowInfo());
        }
    }

    private async void EditItemClick(object sender, RoutedEventArgs _)
    {
        if (sender is not Button { DataContext: EditableWindowInfo item }) return;
        var dialog = new WindowInfoEditDialog { XamlRoot = XamlRoot };
        dialog.SetWindowInfo(item.ToWindowInfo());
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _viewModel.UpdateItem(item, dialog.GetWindowInfo());
        }
    }

    private void DeleteItemClick(object sender, RoutedEventArgs _)
    {
        if (sender is Button { DataContext: EditableWindowInfo item }) _viewModel.RemoveItem(item);
    }

    private void CaptureClick(object _, RoutedEventArgs __)
    {
        _viewModel.StartCaptureCommand.Execute(null);
    }

    private void StopCaptureClick(object _, RoutedEventArgs __)
    {
        _viewModel.StopCaptureCommand.Execute(null);
    }

    private void OnWindowCaptured(WindowInfo info)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            App.Current.MainWindow.Show();
            var dialog = new WindowInfoEditDialog { XamlRoot = XamlRoot };
            dialog.SetWindowInfo(info);
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _viewModel.AddItem(dialog.GetWindowInfo());
            }
        });
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _viewModel.StopCapture();
        _viewModel.WindowCaptured -= OnWindowCaptured;
        base.OnNavigatedFrom(e);
    }
}
