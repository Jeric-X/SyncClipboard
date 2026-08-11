using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class NetworkAccountSwitchPage : UserControl
{
    private readonly NetworkAccountSwitchViewModel _viewModel;

    public NetworkAccountSwitchPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<NetworkAccountSwitchViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
        AttachedToVisualTree += (_, _) => _viewModel.Activate();
        DetachedFromVisualTree += (_, _) => _viewModel.Deactivate();
    }

    private async void AddRuleClick(object? sender, RoutedEventArgs e)
    {
        var editor = _viewModel.CreateRuleEditor();
        var dialog = new NetworkRuleEditDialog(_viewModel, editor);
        if (await dialog.ShowAsync(App.Current.MainWindow) == ContentDialogResult.Primary) _viewModel.AddRuleEditor(editor);
    }

    private async void EditRuleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: NetworkRuleEditor rule }) return;
        var editor = NetworkAccountSwitchViewModel.CloneRuleEditor(rule);
        var dialog = new NetworkRuleEditDialog(_viewModel, editor);
        if (await dialog.ShowAsync(App.Current.MainWindow) == ContentDialogResult.Primary) _viewModel.UpdateRuleEditor(rule, editor);
    }

    private void DeleteRuleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: NetworkRuleEditor rule }) return;
        _viewModel.SelectedRule = rule;
        _viewModel.DeleteRuleCommand.Execute(null);
    }

    private void MoveRuleUpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: NetworkRuleEditor rule }) return;
        _viewModel.SelectedRule = rule;
        _viewModel.MoveUpCommand.Execute(null);
    }

    private void MoveRuleDownClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: NetworkRuleEditor rule }) return;
        _viewModel.SelectedRule = rule;
        _viewModel.MoveDownCommand.Execute(null);
    }

    private void CurrentNetworkStatusClick(object? sender, RoutedEventArgs e) =>
        App.Current.MainWindow.NavigateToNextLevel(PageDefinition.CurrentNetworkStatus);
}
