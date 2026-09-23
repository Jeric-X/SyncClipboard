using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class HotkeyPage : UserControl
{
    public HotkeyViewModel ViewModel { get; }

    public HotkeyPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<HotkeyViewModel>();
        DataContext = ViewModel;
    }

    private async void EditButtonClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.EditingHotkey = Hotkey.Nothing;
        ViewModel.EditingCmdId = (string)((Button)sender!).DataContext!;
        var dialog = (HotkeyEditDialog)Resources["HotkeyEditor"]!;
        dialog.DataContext = ViewModel;
        await dialog.ShowAsync(App.Current.MainWindow);
    }

    private void SettingsExpander_Loaded(object? sender, RoutedEventArgs _)
    {
        if (sender is FASettingsExpander settingsExpander)
        {
            settingsExpander.IsExpanded = true;
        }
    }
}
