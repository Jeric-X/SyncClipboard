using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class SyncSettingPage : UserControl
{
    private readonly SyncSettingViewModel _viewModel;

    public SyncSettingPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<SyncSettingViewModel>();
        this.DataContext = _viewModel;
    }
}
