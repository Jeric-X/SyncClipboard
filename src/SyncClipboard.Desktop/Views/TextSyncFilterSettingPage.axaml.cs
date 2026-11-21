using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.UserConfigs;
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
        var rules = settings.Rules;
        var draft = new EditableTextFilterRule(item?.ToRule() ?? new TextFilterRule(""), settings.FilterMode.Key == "BlackList");
        var dialog = new TextFilterRuleEditDialog { Title = item is null ? Strings.AddRule : Strings.EditRule, DataContext = draft };
        if (await dialog.ShowAsync(owner) != FAContentDialogResult.Primary)
            return;

        if (item is null)
            rules.Add(draft);
        else
            rules[rules.IndexOf(item)] = draft;
    }

    private void DeleteRuleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TextSyncFilterSettingViewModel settings && sender is Button { DataContext: EditableTextFilterRule item })
        {
            settings.Rules.Remove(item);
        }
    }
}
