using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class MainView : UserControl
{
    readonly MainViewModel _viewModel;

    public MainView()
    {
        _viewModel = App.Current.Services.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
    }

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        //
    }

    private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs _)
    {
        var selectedItem = ((ListBox)sender).SelectedItem;
        var page = (PageDefinition)selectedItem!;

        //NavigateTo(page);

        _viewModel.BreadcrumbList.Clear();
        _viewModel.BreadcrumbList.Add(page);

        //if (SplitPane.DisplayMode == SplitViewDisplayMode.Overlay)
        //{
        //    SplitPane.IsPaneOpen = false;
        //}
    }
}
