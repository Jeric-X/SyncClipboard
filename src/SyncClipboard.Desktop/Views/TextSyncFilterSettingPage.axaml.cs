using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class TextSyncFilterSettingPage : UserControl
{
    public TextSyncFilterSettingPage()
    {
        this.InitializeComponent();
        AddHandler(FAFrame.NavigatedToEvent, (_, _) =>
            DataContext = App.Current.Services.GetRequiredService<TextSyncFilterSettingViewModel>(), RoutingStrategies.Direct);
    }

    private void DeleteRuleClick(object sender, RoutedEventArgs _)
    {
        if (DataContext is TextSyncFilterSettingViewModel settings && sender is Button { DataContext: EditableTextFilterRule item })
            settings.Rules.Remove(item);
    }
}
