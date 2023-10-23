using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
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
    }

    private void HyperlinkButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var url = ((HyperlinkButton?)sender)?.Content;
        Sys.OpenWithDefaultApp(url as string);
    }

    private void SettingsExpanderItem_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as SettingsExpanderItem)?.Content is not OpenSourceSoftware software)
        {
            return;
        }

        var path = $"{software.Name}/{software.LicensePath}";

        var mainWindowVM = App.Current.Services.GetService<MainViewModel>();
        var window = App.Current.Services.GetService<IMainWindow>() as MainWindow;
        mainWindowVM?.BreadcrumbList.Add(PageDefinition.License);
        window?.NavigateTo(PageDefinition.License, SlideNavigationTransitionEffect.FromRight, path);
    }
}
