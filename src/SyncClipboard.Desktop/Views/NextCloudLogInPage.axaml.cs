using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.Desktop.Views;

public partial class NextCloudLogInPage : UserControl
{
    private readonly NextCloudLogInViewModel _viewModel;
    public NextCloudLogInPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<NextCloudLogInViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        App.Current.MainWindow.DispableScrollViewer();
    }

    private async void TreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _TreeView.IsEnabled = false;
        await _viewModel.SetChildren((FileTreeViewModel)_TreeView.SelectedItem!);
        _TreeView.IsEnabled = true;
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        App.Current.MainWindow.EnableScrollViewer();
        _viewModel.Cancel();
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var file = (FileTreeViewModel?)_TreeView.SelectedItem;
            if (file is null)
            {
                return;
            }
            _viewModel.SetFolder(file);
        }
        catch (Exception ex)
        {
            App.Current.Services.GetRequiredService<ILogger>().Write(ex.Message);
        }
    }
}
