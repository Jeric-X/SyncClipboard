using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class SyncContentControlPage : Page
{
    private readonly SyncSettingViewModel _viewModel;
    public SyncContentControlPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<SyncSettingViewModel>();
    }
}
