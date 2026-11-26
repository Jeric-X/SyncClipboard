using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Windows.Storage.Pickers;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class ServerConfigPage : Page
{
    private readonly ServerConfigViewModel _viewModel;

    public ServerConfigPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<ServerConfigViewModel>();
        this.InitializeComponent();
    }

    private async void SetCertificatePemPath(object sender, Microsoft.UI.Xaml.RoutedEventArgs _)
    {
        var fileName = await GetFileByPicker((Button)sender, ServerConfigViewModel.CertificatePemFileTypes);
        _viewModel.CertificatePemPath = fileName ?? _viewModel.CertificatePemPath;
    }

    private async void SetCertificatePemKeyPath(object sender, Microsoft.UI.Xaml.RoutedEventArgs _)
    {
        var fileName = await GetFileByPicker((Button)sender, ServerConfigViewModel.CertificatePemKeyFileTypes);
        _viewModel.CertificatePemKeyPath = fileName ?? _viewModel.CertificatePemKeyPath;
    }

    private async void SetCustomConfigurationFilePath(object sender, Microsoft.UI.Xaml.RoutedEventArgs _)
    {
        var fileName = await GetFileByPicker((Button)sender, ServerConfigViewModel.CustomConfigurationFileTypes);
        _viewModel.CustomConfigurationFilePath = fileName ?? _viewModel.CustomConfigurationFilePath;
    }

    private async Task<string?> GetFileByPicker(Button button, IEnumerable<string> types)
    {
        button.IsEnabled = false;
        using ScopeGuard scropGuard = new(() => button.IsEnabled = true);

        var openPicker = new FileOpenPicker(App.Current.MainWindow.AppWindow.Id)
        {
            ViewMode = PickerViewMode.List
        };
        types.ForEach(openPicker.FileTypeFilter.Add);
        openPicker.FileTypeFilter.Add("*");

        var file = await openPicker.PickSingleFileAsync();
        return file?.Path;
    }
}
