using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class CurrentNetworkStatusPage : UserControl
{
    public CurrentNetworkStatusPage()
    {
        var viewModel = App.Current.Services.GetRequiredService<CurrentNetworkStatusViewModel>();
        DataContext = viewModel;
        InitializeComponent();
        AttachedToVisualTree += (_, _) => viewModel.Activate();
        DetachedFromVisualTree += (_, _) => viewModel.Deactivate();
    }
}
