using System;
using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HistorySettingPage : Page
{
    private readonly HistorySettingViewModel _viewModel;
    public HistorySettingPage()
    {
        InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<HistorySettingViewModel>();
    }

    private async void EditShortcutClick(object sender, RoutedEventArgs _)
    {
        if (sender is not Button { DataContext: HistoryShortcutSetting setting })
            return;
        _viewModel.BeginEditShortcut(setting);
        var dialog = (HotkeyEditDialog)Resources["HotkeyEditor"]!;
        dialog.DataContext = _viewModel;
        dialog.Title = setting.Name;
        dialog.XamlRoot = XamlRoot;
        await dialog.ShowAsync();
    }

    private async void ResetShortcutClick(object sender, RoutedEventArgs _)
    {
        if (sender is Button { DataContext: HistoryShortcutSetting setting })
            await _viewModel.ResetShortcutCommand.ExecuteAsync(setting);
    }
}
