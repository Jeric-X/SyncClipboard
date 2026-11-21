using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class SyncFilterSettingPage : Page
{
    private SyncFilterSettingViewModel _viewModel = null!;

    public SyncFilterSettingPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ((MainWindow)App.Current.Services.GetRequiredService<IMainWindow>()).EnableScrollViewer();
        base.OnNavigatedFrom(e);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _viewModel = App.Current.Services.GetRequiredKeyedService<SyncFilterSettingViewModel>(e.Parameter);
        Bindings.Update();
        ((MainWindow)App.Current.Services.GetRequiredService<IMainWindow>()).DispableScrollViewer();
    }

    private async void EditRuleClick(object sender, RoutedEventArgs _)
    {
        var item = (sender as Button)?.DataContext as FilterRuleEditor;
        var draft = _viewModel.CreateEditor(item);
        var dialog = new FilterRuleEditDialog(draft)
        {
            Title = item is null ? Strings.AddRule : Strings.EditRule,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            _viewModel.SaveRule(item, draft);
    }

    private void DeleteRuleClick(object sender, RoutedEventArgs _)
    {
        if (sender is Button { DataContext: FilterRuleEditor item })
            _viewModel.DeleteRule(item);
    }
}
