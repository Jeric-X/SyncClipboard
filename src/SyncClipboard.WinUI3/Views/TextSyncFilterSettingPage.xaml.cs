using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class TextSyncFilterSettingPage : Page
{
    private readonly TextSyncFilterSettingViewModel _viewModel;
    public TextSyncFilterSettingPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<TextSyncFilterSettingViewModel>();
    }

    private void DeleteRuleClick(object sender, RoutedEventArgs _)
    {
        if (sender is Button { DataContext: EditableTextFilterRule item })
            _viewModel.Rules.Remove(item);
    }
}
