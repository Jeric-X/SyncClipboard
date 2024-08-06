using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class FileSyncFilterSettingPage : Page
{
    private readonly FileSyncFilterSettingViewModel _viewModel;

    public FileSyncFilterSettingPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<FileSyncFilterSettingViewModel>();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ((MainWindow)App.Current.Services.GetRequiredService<IMainWindow>()).EnableScrollViewer();
        base.OnNavigatedFrom(e);
    }

    protected override void OnNavigatedTo(NavigationEventArgs _)
    {
        ((MainWindow)App.Current.Services.GetRequiredService<IMainWindow>()).DispableScrollViewer();
    }
}
