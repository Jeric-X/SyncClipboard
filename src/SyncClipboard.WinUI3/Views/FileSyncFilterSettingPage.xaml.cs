using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class FileSyncFilterSettingPage : Page
{
    private readonly FileSyncFilterSettingViewModel _viewModel;

    public FileSyncFilterSettingPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<FileSyncFilterSettingViewModel>();
    }
}
