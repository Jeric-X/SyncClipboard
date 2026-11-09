using Avalonia.Controls;
using SyncClipboard.Core.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace SyncClipboard.Desktop.Utilities;

internal static class HistoryTagNavigation
{
    internal static IReadOnlyList<Control> GetCandidates(ItemsControl items, ScrollViewer scroll, bool expanded)
    {
        var candidates = new List<Control>();
        if (items.ItemsPanelRoot is not { } panel)
            return candidates;

        foreach (var child in panel.Children)
        {
            if (!child.IsVisible || child.Bounds.Height <= 0 || child.DataContext is not HistoryTagFilter)
                continue;
            var bounds = child.Bounds;
            if (expanded || (bounds.Y < scroll.Offset.Y + scroll.Viewport.Height - 0.5
                && bounds.Bottom > scroll.Offset.Y + 0.5))
                candidates.Add(child);
        }
        return candidates;
    }

    internal static void Navigate(ItemsControl items, ScrollViewer scroll, HistoryViewModel model,
        bool expanded, bool forward, bool vertical = false)
    {
        var candidates = GetCandidates(items, scroll, expanded);
        var target = vertical
            ? model.SelectTagInAdjacentRow(candidates.Select(child =>
            {
                var bounds = child.Bounds;
                return ((HistoryTagFilter)child.DataContext!, bounds.X, bounds.Y, bounds.Width);
            }).ToArray(), forward)
            : model.SelectAdjacentTag(candidates.Select(child => (HistoryTagFilter)child.DataContext!).ToArray(), forward);
        if (target is not null && expanded)
            candidates.First(child => ReferenceEquals(child.DataContext, target)).BringIntoView();
    }
}
