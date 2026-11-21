using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.I18n;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class TextSyncFilterSettingPage : Page
{
    private readonly TextSyncFilterSettingViewModel _viewModel;

    public TextSyncFilterSettingPage()
    {
        this.InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<TextSyncFilterSettingViewModel>();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ((MainWindow)App.Current.Services.GetRequiredService<IMainWindow>()).EnableScrollViewer();
        base.OnNavigatedFrom(e);
    }

    protected override void OnNavigatedTo(NavigationEventArgs _)
    {
        ((MainWindow)App.Current.Services.GetRequiredService<IMainWindow>()).DispableScrollViewer();
    }

    private async void EditRuleClick(object sender, RoutedEventArgs _)
    {
        var item = (sender as Button)?.DataContext as EditableTextFilterRule;
        var draft = _viewModel.CreateEditor(item);
        var dialog = new TextFilterRuleEditDialog(draft)
        {
            Title = item is null ? Strings.AddRule : Strings.EditRule,
            XamlRoot = XamlRoot,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (_viewModel.TrySaveRule(item, draft))
                return;
            args.Cancel = true;
            item = null;
            dialog.Title = Strings.TextFilterRuleChanged;
        };
        await dialog.ShowAsync();
    }

    private void DeleteRuleClick(object sender, RoutedEventArgs _)
    {
        if (sender is Button { DataContext: EditableTextFilterRule item })
            _viewModel.DeleteRule(item);
    }
}
