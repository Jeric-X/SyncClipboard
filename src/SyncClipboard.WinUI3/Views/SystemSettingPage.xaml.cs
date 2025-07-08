using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

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

    private void ShowProxySettingDialog(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var dialog = new ProxySettingDialog
        {
            XamlRoot = this.XamlRoot
        };
        _ = dialog.ShowAsync();
    }
}
