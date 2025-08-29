using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class HistorySettingPage : UserControl
{
    private readonly HistorySettingViewModel _viewModel;
    public HistorySettingPage()
    {
        InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<HistorySettingViewModel>();
        DataContext = _viewModel;
    }
}