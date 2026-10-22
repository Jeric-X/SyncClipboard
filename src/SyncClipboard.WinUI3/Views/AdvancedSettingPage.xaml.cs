using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class AdvancedSettingPage : Page
{
    private readonly SystemSettingViewModel _viewModel;

    public AdvancedSettingPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<SystemSettingViewModel>();
        InitializeComponent();
    }
}
