using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class NetworkAccountSwitchPage : Page
{
    private readonly NetworkAccountSwitchViewModel _viewModel;

    public NetworkAccountSwitchPage()
    {
        InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<NetworkAccountSwitchViewModel>();
        Loaded += (_, _) => _viewModel.Activate();
        Unloaded += (_, _) => _viewModel.Deactivate();
    }

    private async void AddRuleClick(object _, RoutedEventArgs _1)
    {
        var editor = _viewModel.CreateRuleEditor();
        var dialog = new NetworkRuleEditDialog(_viewModel, editor) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) _viewModel.AddRuleEditor(editor);
    }

    private async void EditRuleClick(object sender, RoutedEventArgs _)
    {
        if (sender is not Button { DataContext: NetworkRuleEditor rule }) return;
        var editor = NetworkAccountSwitchViewModel.CloneRuleEditor(rule);
        var dialog = new NetworkRuleEditDialog(_viewModel, editor) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) _viewModel.UpdateRuleEditor(rule, editor);
    }

    private void DeleteRuleClick(object sender, RoutedEventArgs _)
    {
        if (sender is not Button { DataContext: NetworkRuleEditor rule }) return;
        _viewModel.SelectedRule = rule;
        _viewModel.DeleteRuleCommand.Execute(null);
    }

    private void MoveRuleUpClick(object sender, RoutedEventArgs _)
    {
        if (sender is not Button { DataContext: NetworkRuleEditor rule }) return;
        _viewModel.SelectedRule = rule;
        _viewModel.MoveUpCommand.Execute(null);
    }

    private void MoveRuleDownClick(object sender, RoutedEventArgs _)
    {
        if (sender is not Button { DataContext: NetworkRuleEditor rule }) return;
        _viewModel.SelectedRule = rule;
        _viewModel.MoveDownCommand.Execute(null);
    }

    private void CurrentNetworkStatusClick(object _, RoutedEventArgs _1) =>
        App.Current.Services.GetRequiredService<IMainWindow>()
            .NavigateToNextLevel(PageDefinition.CurrentNetworkStatus, null);
}
