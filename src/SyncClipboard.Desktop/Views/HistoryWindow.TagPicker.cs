using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
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
            var manager = App.Current.Services.GetRequiredService<HistoryManager>();
            var snapshot = await Task.Run(() => manager.GetSyncSnapshotAsync(key));
            if (snapshot is null || snapshot.IsDeleted)
                return;
            var counts = await Task.Run(() => manager.GetTagCountsAsync());
            if (bar.Record?.Key != key || !bar.IsEffectivelyVisible || !IsVisible || TopLevel.GetTopLevel(bar) != this)
                return;

            var picker = new HistoryTagPickerViewModel(counts.Keys, snapshot.Tags, (name, add) => Task.Run(async () =>
            {
                await manager.EditTagsAsync([key], add ? [name] : [], add ? [] : [name]);
                if (await manager.GetSyncSnapshotAsync(key) is not { IsDeleted: false })
                    throw new InvalidOperationException(Strings.HistoryTagsNoSelection);
            }));
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
        catch (Exception ex)
        {
            await new AvaloniaDialog(this).ShowMessageAsync(Strings.HistoryTagsQuickAdd, ex.Message);
        }
        finally
        {
            bar.AddTagAnchor.IsEnabled = true;
        }
    }
}
