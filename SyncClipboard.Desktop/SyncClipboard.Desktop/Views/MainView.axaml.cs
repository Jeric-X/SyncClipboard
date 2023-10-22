using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;
using System;
using System.Collections.Generic;

namespace SyncClipboard.Desktop.Views;

public partial class MainView : UserControl
{
    readonly MainViewModel _viewModel;


    public static List<PageDefinition> MainWindowPage { get; } = new()
    {
        PageDefinition.SyncSetting,
        PageDefinition.ServiceStatus,
        PageDefinition.SystemSetting,
        PageDefinition.About,
    };

    public MainView()
    {
        _viewModel = App.Current.Services.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
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

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (_viewModel.BreadcrumbList.Count - 1 == args.Index)
        {
            return;
        }

        for (int i = _viewModel.BreadcrumbList.Count - 1; i >= args.Index + 1; i--)
        {
            _viewModel.BreadcrumbList.RemoveAt(i);
        }

        NavigateTo(_viewModel.BreadcrumbList[args.Index], SlideNavigationTransitionEffect.FromLeft);
    }

    private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs _)
    {
        var selectedItem = ((ListBox)sender).SelectedItem;
        var page = (PageDefinition)selectedItem!;

        NavigateTo(page);

        _viewModel.BreadcrumbList.Clear();
        _viewModel.BreadcrumbList.Add(page);
    }
}
