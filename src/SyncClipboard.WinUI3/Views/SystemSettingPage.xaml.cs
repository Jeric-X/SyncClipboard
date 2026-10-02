using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class SystemSettingPage : Page
{
    private readonly SystemSettingViewModel _viewModel;
    private readonly IMainWindowDialog _dialog;

    public SystemSettingPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<SystemSettingViewModel>();
        _dialog = App.Current.Services.GetRequiredService<IMainWindowDialog>();
        this.DataContext = _viewModel;
    }

    private void ShowProxySettingDialog(object _0, Microsoft.UI.Xaml.RoutedEventArgs _1)
    {
        var dialog = new ProxySettingDialog
        {
            XamlRoot = this.XamlRoot
        };
        _ = dialog.ShowAsync();
    }

    private async void ChangeAppDataFolder(object sender, RoutedEventArgs _)
    {
        if (sender is Button button)
            button.IsEnabled = false;
        try
        {
            var folder = await _dialog.PickFolderAsync(Strings.AppDataFolder);
            if (folder is null)
                return;

            await _viewModel.ChangeAppDataFolderAsync(folder);
        }
        finally
        {
            if (sender is Button btn)
                btn.IsEnabled = true;
        }
    }
}
