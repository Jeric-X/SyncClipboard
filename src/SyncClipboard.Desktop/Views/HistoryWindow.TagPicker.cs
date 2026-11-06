using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Desktop.Services;
using System;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Views;

public partial class HistoryWindow
{
    private async void AddRecordTag(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (e.Source is not RecordControlBar { Record: { } record } bar)
            return;

        var key = record.Key;
        bar.AddTagAnchor.IsEnabled = false;
        try
        {
            var picker = await CreateTagPickerAsync(key);
            if (picker is null || bar.Record?.Key != key || !bar.IsEffectivelyVisible || !IsVisible || TopLevel.GetTopLevel(bar) != this)
                return;
            ShowTagPicker(bar, key, picker);
        }
        catch (Exception ex)
        {
            await new AvaloniaDialog(this).ShowMessageAsync(Strings.HistoryTagsQuickAdd, ex.Message);
        }
        finally
        {
            bar.AddTagAnchor.IsEnabled = true;
        }
    }

    private static async Task<HistoryTagPickerViewModel?> CreateTagPickerAsync(HistoryRecordKey key)
    {
        var manager = App.Current.Services.GetRequiredService<HistoryManager>();
        var snapshot = await Task.Run(() => manager.GetSyncSnapshotAsync(key));
        if (snapshot is null || snapshot.IsDeleted)
            return null;
        var counts = await Task.Run(() => manager.GetTagCountsAsync());
        return new HistoryTagPickerViewModel(counts.Keys, snapshot.Tags, (name, add) => Task.Run(async () =>
        {
            await manager.EditTagsAsync([key], add ? [name] : [], add ? [] : [name]);
            if (await manager.GetSyncSnapshotAsync(key) is not { IsDeleted: false })
                throw new InvalidOperationException(Strings.HistoryTagsNoSelection);
        }));
    }

    private static void ShowTagPicker(RecordControlBar bar, HistoryRecordKey key, HistoryTagPickerViewModel picker)
    {
        var flyout = new HistoryTagPickerView(picker).ShowAt(bar.AddTagAnchor);
        void RecordChanged(object? _, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == RecordControlBar.RecordProperty && bar.Record?.Key != key)
                flyout.Hide();
        }
        void Detached(object? _, VisualTreeAttachmentEventArgs __) => flyout.Hide();
        bar.PropertyChanged += RecordChanged;
        bar.DetachedFromVisualTree += Detached;
        flyout.Closed += (_, _) =>
        {
            bar.PropertyChanged -= RecordChanged;
            bar.DetachedFromVisualTree -= Detached;
        };
    }
}
