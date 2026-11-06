using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels.Sub;
using System;

namespace SyncClipboard.WinUI3.Controls;

public sealed partial class HistoryRecordTagChip : UserControl
{
    public event EventHandler<HistoryPreviewTag>? RemoveRequested;

    public HistoryRecordTagChip() => InitializeComponent();

    internal void RemoveTag_Click(object _, RoutedEventArgs __)
    {
        if (DataContext is HistoryPreviewTag tag)
        {
            RemoveRequested?.Invoke(this, tag);
        }
    }
}
