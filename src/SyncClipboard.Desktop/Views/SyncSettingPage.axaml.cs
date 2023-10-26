using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using System;
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
            TextBoxName = Strings.Port,
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
}
