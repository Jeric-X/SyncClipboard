using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class FileSyncFilterSettingPage : UserControl
{
    private readonly FileSyncFilterSettingViewModel _viewModel;
    public FileSyncFilterSettingPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<FileSyncFilterSettingViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        App.Current.MainWindow.EnableScrollViewer();
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        App.Current.MainWindow.DispableScrollViewer();
    }
}
