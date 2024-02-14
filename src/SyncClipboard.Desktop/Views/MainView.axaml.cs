using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.Desktop.Views;

public partial class MainView : UserControl
{
    readonly MainViewModel _viewModel;

    public MainView()
    {
        _viewModel = App.Current.Services.GetRequiredService<MainViewModel>();

        var config = App.Current.Services.GetRequiredService<ConfigManager>();
        config.ListenConfig<ProgramConfig>(HandleDiagnoseModeChanged);
        if (config.GetConfig<ProgramConfig>().DiagnoseMode is true)
        {
            _viewModel.MainWindowPage.Add(PageDefinition.Diagnose);
        }

        DataContext = _viewModel;
        InitializeComponent();

        _MenuList.SelectedIndex = 0;
    }

    private void HandleDiagnoseModeChanged(ProgramConfig config)
    {
        if (config.DiagnoseMode is true)
        {
            if (_viewModel.MainWindowPage.Contains(PageDefinition.Diagnose) is false)
                _viewModel.MainWindowPage.Add(PageDefinition.Diagnose);
        }
        else
        {
            if (_viewModel.MainWindowPage.Contains(PageDefinition.Diagnose))
                _viewModel.MainWindowPage.Remove(PageDefinition.Diagnose);
        }
    }

    internal void NavigateTo(
        PageDefinition page,
        SlideNavigationTransitionEffect effect = SlideNavigationTransitionEffect.FromBottom,
        object? parameter = null)
    {
        string pageName = "SyncClipboard.Desktop.Views." + page.Name + "Page";
        Type? pageType = Type.GetType(pageName);
        SettingContentFrame.Navigate(pageType, parameter, new SlideNavigationTransitionInfo { Effect = effect });
    }

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar _, BreadcrumbBarItemClickedEventArgs args)
    {
        _viewModel.BreadcrumbBarClicked(args.Index);
    }

    private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs _)
    {
        var selectedItem = ((ListBox)sender).SelectedItem;
        var page = (PageDefinition)selectedItem!;

        NavigateTo(page);

        _viewModel.BreadcrumbList.Clear();
        _viewModel.BreadcrumbList.Add(page);
    }

    internal void DispableScrollViewer()
    {
        //_ScrollViewer.IsEnabled = false;
        _ScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    internal void EnableScrollViewer()
    {
        _ScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }
}
