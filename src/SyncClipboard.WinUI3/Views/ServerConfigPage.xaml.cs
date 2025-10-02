using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class ServerConfigPage : Page
{
    private readonly ServerConfigViewModel _viewModel;

    public ServerConfigPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<ServerConfigViewModel>();
        this.InitializeComponent();
    }

    [RelayCommand]
    private async Task SetServerAsync()
    {
        _ServerSettingDialog.Password = _viewModel.ServerConfig.Password;
        _ServerSettingDialog.UserName = _viewModel.ServerConfig.UserName;
        _ServerSettingDialog.Url = _viewModel.ServerConfig.Port.ToString();
        await _ServerSettingDialog.ShowAsync();
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

        var openPicker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List
        };
        types.ForEach(openPicker.FileTypeFilter.Add);
        openPicker.FileTypeFilter.Add("*");

        // Retrieve the window handle (HWND) of the current WinUI 3 window.
        var window = ((MainWindow)App.Current.Services.GetRequiredService<IMainWindow>());
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        // Initialize the file picker with the window handle (HWND).
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        var file = await openPicker.PickSingleFileAsync();
        return file?.Path;
    }

    private void ServerSettingDialog_OkClick(ContentDialog _, ContentDialogButtonClickEventArgs args)
    {
        var res = _viewModel.SetServerConfig(_ServerSettingDialog.Url, _ServerSettingDialog.UserName, _ServerSettingDialog.Password);
        if (string.IsNullOrEmpty(res))
        {
            _ServerSettingDialog.ErrorTip = "";
            return;
        }

        _ServerSettingDialog.ErrorTip = res;
        args.Cancel = true;
    }
}