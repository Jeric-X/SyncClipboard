using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;
using System.ComponentModel;
using System.Reflection;

namespace SyncClipboard.Desktop.Views;

public partial class AddAccountPage : UserControl
{
    private readonly AddAccountViewModel _viewModel;

    public AddAccountPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<AddAccountViewModel>();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        InitializeComponent();
        UpdateConfigurationContent();
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AddAccountViewModel.NavigationInfo))
        {
            UpdateConfigurationContent();
        }
    }

    private void UpdateConfigurationContent()
    {
        var currentAssembly = Assembly.GetExecutingAssembly();
        var pageTypeName = $"SyncClipboard.Desktop.Views.{_viewModel.ConfigurationPageName}";
        var pageType = currentAssembly.GetType(pageTypeName);

        var accountManager = App.Current.Services.GetRequiredService<AccountManager>();
        var accountId = accountManager.CreateAccountId(_viewModel.SelectedType);
        var accountConfig = new AccountConfig { AccountId = accountId, AccountType = _viewModel.SelectedType };

        if (pageType != null && pageType.IsSubclassOf(typeof(UserControl)))
        {
            ConfigurationFrame.Navigate(pageType, accountConfig);
        }
        else
        {
            ConfigurationFrame.Navigate(typeof(UserControl), accountConfig);
        }
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        ConfigurationFrame.Navigate(typeof(UserControl), _viewModel.SelectedType);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        UpdateConfigurationContent();
    }
}