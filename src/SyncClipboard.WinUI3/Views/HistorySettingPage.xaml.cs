using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HistorySettingPage : Page
{
    private readonly HistorySettingViewModel _viewModel;
    public HistorySettingPage()
    {
        InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<HistorySettingViewModel>();
    }
}