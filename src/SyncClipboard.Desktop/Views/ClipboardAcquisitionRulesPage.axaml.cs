using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class ClipboardAcquisitionRulesPage : UserControl
{
    private readonly ClipboardAcquisitionRulesViewModel _viewModel;

    public ClipboardAcquisitionRulesPage()
    {
        _viewModel = App.Current.Services.GetRequiredService<ClipboardAcquisitionRulesViewModel>();
        DataContext = _viewModel;
        InitializeComponent();
    }
}
