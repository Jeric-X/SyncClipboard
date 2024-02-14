using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;
using Avalonia.Interactivity;

namespace SyncClipboard.Desktop.Views;

public partial class HotkeyPage : UserControl
{
    private readonly HotkeyViewModel _viewModel;

    public HotkeyPage()
    {
        InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<HotkeyViewModel>();
        DataContext = _viewModel;
    }

    private void EditButtonClick(object? sender, RoutedEventArgs e)
    {

    }
    private void SetToDefaultButtonClick(object? sender, RoutedEventArgs e)
    {

    }

    private void SettingsExpander_Loaded(object? sender, RoutedEventArgs _)
    {
        if (sender is SettingsExpander settingsExpander)
        {
            settingsExpander.IsExpanded = true;
        }
    }
}
