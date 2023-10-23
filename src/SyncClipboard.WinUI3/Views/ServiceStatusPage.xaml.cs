using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class ServiceStatusPage : Page
{
    private readonly ServiceStatusViewModel _viewModel;

    public ServiceStatusPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<ServiceStatusViewModel>();
    }
}
