using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class HistorySettingPage : UserControl
{
    private readonly HistorySettingViewModel _viewModel;
    public HistorySettingPage()
    {
        InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<HistorySettingViewModel>();
        DataContext = _viewModel;
    }

    private async void EditShortcutClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HistoryShortcutSetting setting })
            return;
        _viewModel.BeginEditShortcut(setting);
        var dialog = (HotkeyEditDialog)Resources["HotkeyEditor"]!;
        dialog.DataContext = _viewModel;
        dialog.Title = setting.Name;
        await dialog.ShowAsync(App.Current.MainWindow);
    }

    private async void ResetShortcutClick(object? sender, RoutedEventArgs _)
    {
        if (sender is Button { DataContext: HistoryShortcutSetting setting })
            await _viewModel.ResetShortcutCommand.ExecuteAsync(setting);
    }
}
