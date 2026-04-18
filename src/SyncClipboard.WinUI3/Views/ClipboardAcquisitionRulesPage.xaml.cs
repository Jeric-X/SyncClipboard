using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class ClipboardAcquisitionRulesPage : Page
{
    private readonly ClipboardAcquisitionRulesViewModel _viewModel;

    public ClipboardAcquisitionRulesPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<ClipboardAcquisitionRulesViewModel>();
    }
}
