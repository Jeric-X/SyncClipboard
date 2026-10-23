using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class AdvancedSettingPage : Page
{
    private readonly AdvancedSettingViewModel _viewModel;

    public AdvancedSettingPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<AdvancedSettingViewModel>();
        InitializeComponent();
    }
}
