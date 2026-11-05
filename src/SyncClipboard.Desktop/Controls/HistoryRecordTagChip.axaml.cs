using Avalonia.Controls;
using Avalonia.Interactivity;
using SyncClipboard.Core.ViewModels.Sub;
using System;

namespace SyncClipboard.Desktop.Controls;

public partial class HistoryRecordTagChip : UserControl
{
    public event EventHandler<HistoryPreviewTag>? RemoveRequested;

    public HistoryRecordTagChip() => InitializeComponent();

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: HistoryPreviewTag tag })
            RemoveRequested?.Invoke(this, tag);
    }
}
