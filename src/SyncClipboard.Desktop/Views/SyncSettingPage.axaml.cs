using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Views;

public partial class SyncSettingPage : UserControl
{
    private readonly SyncSettingViewModel _viewModel;
    private ServerSettingDialog? _serverConfigDialog;
    private ServerSettingDialog? _clientConfigDialog;

    public SyncSettingPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<SyncSettingViewModel>();
        this.DataContext = _viewModel;
    }

    private void SetServerConfig(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _serverConfigDialog = new ServerSettingDialog
        {
            TextBoxName = Strings.Port,
            Password = _viewModel.ServerConfig.Password,
            UserName = _viewModel.ServerConfig.UserName,
            Url = _viewModel.ServerConfig.Port.ToString()
        };
        var dialog = new ContentDialog
        {
            Title = Strings.Settings,
            PrimaryButtonText = Strings.Confirm,
            CloseButtonText = Strings.Cancel,
            Content = _serverConfigDialog
        };
        dialog.PrimaryButtonClick += ServerSettingDialog_OkClick;
        dialog.ShowAsync();
    }

    private void SetClientConfig(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _clientConfigDialog = new ServerSettingDialog
        {
            TextBoxName = Strings.Address,
            Password = _viewModel.ClientConfig.Password,
            UserName = _viewModel.ClientConfig.UserName,
            Url = _viewModel.ClientConfig.RemoteURL.ToString()
        };
        var dialog = new ContentDialog
        {
            Title = Strings.Settings,
            PrimaryButtonText = Strings.Confirm,
            CloseButtonText = Strings.Cancel,
            Content = _clientConfigDialog
        };
        dialog.PrimaryButtonClick += ClientSettingDialog_OkClick;
        dialog.ShowAsync();
    }

    private void ServerSettingDialog_OkClick(ContentDialog _, ContentDialogButtonClickEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(_serverConfigDialog);
        var res = _viewModel.SetServerConfig(_serverConfigDialog.Url, _serverConfigDialog.UserName, _serverConfigDialog.Password);
        if (string.IsNullOrEmpty(res))
        {
            _serverConfigDialog.ErrorTip = "";
            return;
        }

        _serverConfigDialog.ErrorTip = res;
        args.Cancel = true;
        return;
    }

    private void ClientSettingDialog_OkClick(ContentDialog _, ContentDialogButtonClickEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(_clientConfigDialog);
        var res = _viewModel.SetClientConfig(_clientConfigDialog.Url, _clientConfigDialog.UserName, _clientConfigDialog.Password);
        if (string.IsNullOrEmpty(res))
        {
            _clientConfigDialog.ErrorTip = "";
            return;
        }

        _clientConfigDialog.ErrorTip = res;
        args.Cancel = true;
        return;
    }

    private async Task<string?> GetFileByPicker(Button button, IEnumerable<string> types)
    {
        button.IsEnabled = false;
        using ScopeGuard scropGuard = new(() => button.IsEnabled = true);

        var filePickerOpenOptio = new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new(string.Join(',', types.Select(type => type[1..]))) { Patterns = types.ToList() }, new("All") { Patterns = ["*"] }]
        };

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(filePickerOpenOptio);
        if (files is null || !files.Any())
        {
            return null;
        }

        return files[0].Path.LocalPath;
    }

    private async void SetCertificatePemPath(object sender, Avalonia.Interactivity.RoutedEventArgs _)
    {
        var fileName = await GetFileByPicker((Button)sender, SyncSettingViewModel.CertificatePemFileTypes);
        _viewModel.CertificatePemPath = fileName ?? _viewModel.CertificatePemPath;
    }

    private async void SetCertificatePemKeyPath(object sender, Avalonia.Interactivity.RoutedEventArgs _)
    {
        var fileName = await GetFileByPicker((Button)sender, SyncSettingViewModel.CertificatePemKeyFileTypes);
        _viewModel.CertificatePemKeyPath = fileName ?? _viewModel.CertificatePemKeyPath;
    }

    private async void SetCustomConfigurationFilePath(object sender, Avalonia.Interactivity.RoutedEventArgs _)
    {
        var fileName = await GetFileByPicker((Button)sender, SyncSettingViewModel.CustomConfigurationFileTypes);
        _viewModel.CustomConfigurationFilePath = fileName ?? _viewModel.CustomConfigurationFilePath;
    }
}
