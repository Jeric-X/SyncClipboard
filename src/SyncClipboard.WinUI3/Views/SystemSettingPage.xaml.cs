using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using System;
using Windows.Storage.Pickers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class SystemSettingPage : Page
{
    private readonly SystemSettingViewModel _viewModel;

    public SystemSettingPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<SystemSettingViewModel>();
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
        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        folderPicker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder is null) return;

        if (sender is Button button) button.IsEnabled = false;
        try
        {
            await _viewModel.ChangeAppDataFolderAsync(folder.Path);
        }
        finally
        {
            if (sender is Button btn) btn.IsEnabled = true;
        }
    }
}
