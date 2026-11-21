using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class TextSyncFilterSettingPage : UserControl
{
    public TextSyncFilterSettingPage()
    {
        InitializeComponent();
        AddHandler(FAFrame.NavigatedToEvent, (_, _) =>
        {
            DataContext = App.Current.Services.GetRequiredService<TextSyncFilterSettingViewModel>();
            App.Current.MainWindow.DispableScrollViewer();
        }, RoutingStrategies.Direct);
        AddHandler(FAFrame.NavigatedFromEvent, (_, _) => App.Current.MainWindow.EnableScrollViewer(), RoutingStrategies.Direct);
    }

    private async void EditRuleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextSyncFilterSettingViewModel settings || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var item = (sender as Button)?.DataContext as EditableTextFilterRule;
        var draft = settings.CreateEditor(item);
        var dialog = new FilterRuleEditDialog(draft) { Title = item is null ? Strings.AddRule : Strings.EditRule };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (settings.TrySaveRule(item, draft))
                return;
            args.Cancel = true;
            item = null;
            dialog.Title = Strings.TextFilterRuleChanged;
        };
        await dialog.ShowAsync(owner);
    }

    private void DeleteRuleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TextSyncFilterSettingViewModel settings && sender is Button { DataContext: EditableTextFilterRule item })
        {
            settings.DeleteRule(item);
        }
    }
}
