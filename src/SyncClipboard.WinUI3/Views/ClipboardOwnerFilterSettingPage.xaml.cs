using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class ClipboardOwnerFilterSettingPage : Page
{
    private readonly ClipboardOwnerFilterSettingViewModel _viewModel;

    public ClipboardOwnerFilterSettingPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<ClipboardOwnerFilterSettingViewModel>();
        _viewModel.OnClipboardOwnerCaptured += OnClipboardOwnerCaptured;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _viewModel.StopListening();
        _viewModel.OnClipboardOwnerCaptured -= OnClipboardOwnerCaptured;
        ((MainWindow)App.Current.Services.GetRequiredService<IMainWindow>()).EnableScrollViewer();
        base.OnNavigatedFrom(e);
    }

    protected override void OnNavigatedTo(NavigationEventArgs _)
    {
        ((MainWindow)App.Current.Services.GetRequiredService<IMainWindow>()).DispableScrollViewer();
    }

    private async void AddItemClick(object _, RoutedEventArgs _1)
    {
        var dialog = new WindowInfoEditDialog
        {
            XamlRoot = this.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _viewModel.AddItem(dialog.GetWindowInfo());
        }
    }

    private async void EditItemClick(object sender, RoutedEventArgs _)
    {
        if (sender is Button button && button.DataContext is EditableWindowInfo item)
        {
            var dialog = new WindowInfoEditDialog
            {
                XamlRoot = this.XamlRoot
            };
            dialog.SetWindowInfo(item.ToWindowInfo());
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _viewModel.UpdateItem(item, dialog.GetWindowInfo());
            }
        }
    }

    private void DeleteItemClick(object sender, RoutedEventArgs _)
    {
        if (sender is Button button && button.DataContext is EditableWindowInfo item)
        {
            _viewModel.RemoveItem(item);
        }
    }

    private void CaptureClick(object _, RoutedEventArgs _1)
    {
        _viewModel.StartListening();
    }

    private void OnClipboardOwnerCaptured(ForegroundWindowInfo info)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            App.Current.MainWindow.Show();

            var dialog = new WindowInfoEditDialog
            {
                XamlRoot = this.XamlRoot
            };
            dialog.SetWindowInfo(info);
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _viewModel.AddItem(dialog.GetWindowInfo());
            }
        });
    }
}
