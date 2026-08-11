using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class CurrentNetworkStatusPage : Page
{
    private readonly CurrentNetworkStatusViewModel _viewModel;

    public CurrentNetworkStatusPage()
    {
        InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<CurrentNetworkStatusViewModel>();
        Loaded += (_, _) => _viewModel.Activate();
        Unloaded += (_, _) => _viewModel.Deactivate();
    }
}
