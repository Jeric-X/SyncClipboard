using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class AccountConfigEditPage : UserControl
{
    private readonly AccountConfigEditViewModel _viewModel;

    public AccountConfigEditPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<AccountConfigEditViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, Avalonia.Interactivity.RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, Avalonia.Interactivity.RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        App.Current.MainWindow.DispableScrollViewer();
        if (e.Parameter is AccountConfig accountConfig)
        {
            LoadTypeProperties(accountConfig);
        }
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        App.Current.MainWindow.EnableScrollViewer();
        _viewModel.CancelTestCommand.Execute(null);
    }

    public void LoadTypeProperties(AccountConfig accountConfig)
    {
        _viewModel.LoadProperties(accountConfig);
    }

    public AccountConfigEditViewModel ViewModel => _viewModel;
}