using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;
using System.ComponentModel;
using System.Reflection;

namespace SyncClipboard.WinUI3.Views;

/// <summary>
/// 添加账号页面
/// </summary>
public sealed partial class AddAccountPage : Page
{
    private readonly AddAccountViewModel _viewModel;

    public AddAccountPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<AddAccountViewModel>();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        this.DataContext = _viewModel;
        UpdateConfigurationContent();
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
        var pageTypeName = $"SyncClipboard.WinUI3.Views.{_viewModel.ConfigurationPageName}";
        var pageType = currentAssembly.GetType(pageTypeName);

        var accountManager = App.Current.Services.GetRequiredService<AccountManager>();
        var accountId = accountManager.CreateAccountId(_viewModel.SelectedType);
        var accountConfig = new AccountConfig { AccountId = accountId, AccountType = _viewModel.SelectedType };

        if (pageType != null && pageType.IsSubclassOf(typeof(Page)))
        {
            ConfigurationFrame.Navigate(pageType, accountConfig);
        }
        else
        {
            ConfigurationFrame.Navigate(typeof(AccountConfigEditPage), accountConfig);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ConfigurationFrame.Navigate(typeof(Page));
        base.OnNavigatedFrom(e);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        UpdateConfigurationContent();
        base.OnNavigatedTo(e);
    }
}