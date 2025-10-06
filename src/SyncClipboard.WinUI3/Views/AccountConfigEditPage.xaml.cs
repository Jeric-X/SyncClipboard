using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class AccountConfigEditPage : Page
{
    private readonly AccountConfigEditViewModel _viewModel;

    public AccountConfigEditPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<AccountConfigEditViewModel>();
        this.InitializeComponent();
        this.DataContext = _viewModel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        App.Current.MainWindow.DispableScrollViewer();
        base.OnNavigatedTo(e);

        if (e.Parameter is AccountConfig accountConfig)
        {
            LoadTypeProperties(accountConfig);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Current.MainWindow.EnableScrollViewer();
        base.OnNavigatedFrom(e);
        _viewModel.CancelTestCommand.Execute(null);
    }

    public void LoadTypeProperties(AccountConfig accountConfig)
    {
        _viewModel.LoadProperties(accountConfig);
    }

    public AccountConfigEditViewModel ViewModel => _viewModel;
}