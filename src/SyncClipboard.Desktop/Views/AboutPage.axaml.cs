using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class AboutPage : UserControl
{
    private readonly AboutViewModel _viewModel;
    public AboutPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<AboutViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
        _AppInfo.ActualThemeVariantChanged += ThemeChanged;
    }

    private void ThemeChanged(object? sender, System.EventArgs e)
    {
        _AppInfo.IconSource = (FAImageIconSource)App.Current.Resources["AppLogoSource"]!;
    }

    private void HyperlinkButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var url = ((HyperlinkButton?)sender)?.Content;
        Sys.OpenWithDefaultApp(url as string);
    }

    private void SettingsExpanderItem_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as FASettingsExpanderItem)?.Content is not OpenSourceSoftware software)
        {
            return;
        }

        var path = software.LicensePath;
        App.Current.MainWindow.NavigateToNextLevel(PageDefinition.License, path);
    }
}
