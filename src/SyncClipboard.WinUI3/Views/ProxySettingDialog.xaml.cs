using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views;

public sealed partial class ProxySettingDialog : ContentDialog
{
    private readonly ProxySettingViewModel _viewModel;
    public ProxySettingDialog()
    {
        _viewModel = App.Current.Services.GetRequiredService<ProxySettingViewModel>();
        InitializeComponent();
    }
}
