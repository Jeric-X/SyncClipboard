using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class SyncFilterSettingPage : UserControl
{
    public SyncFilterSettingPage()
    {
        InitializeComponent();
        AddHandler(FAFrame.NavigatedToEvent, (_, e) =>
        {
            DataContext = App.Current.Services.GetRequiredKeyedService<SyncFilterSettingViewModel>(e.Parameter);
            App.Current.MainWindow.DispableScrollViewer();
        }, RoutingStrategies.Direct);
        AddHandler(FAFrame.NavigatedFromEvent, (_, _) => App.Current.MainWindow.EnableScrollViewer(), RoutingStrategies.Direct);
    }

    private async void EditRuleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SyncFilterSettingViewModel settings || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var item = (sender as Button)?.DataContext as FilterRuleEditor;
        var draft = settings.CreateEditor(item);
        var dialog = new FilterRuleEditDialog(draft) { Title = item is null ? Strings.AddRule : Strings.EditRule };
        if (await dialog.ShowAsync(owner) == FAContentDialogResult.Primary)
            settings.SaveRule(item, draft);
    }

    private void DeleteRuleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SyncFilterSettingViewModel settings && sender is Button { DataContext: FilterRuleEditor item })
        {
            settings.DeleteRule(item);
        }
    }
}
