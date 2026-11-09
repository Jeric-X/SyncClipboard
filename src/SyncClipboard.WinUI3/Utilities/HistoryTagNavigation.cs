using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SyncClipboard.Core.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace SyncClipboard.WinUI3.Utilities;

internal static class HistoryTagNavigation
{
    internal static IReadOnlyList<FrameworkElement> GetCandidates(ItemsControl items, ScrollViewer scroll, bool expanded)
    {
        var candidates = new List<FrameworkElement>();
        if (items.ItemsPanelRoot is not { } panel)
            return candidates;

        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            var bounds = LayoutInformation.GetLayoutSlot(child);
            if (child.Visibility != Visibility.Visible || bounds.Height <= 0 || child.DataContext is not HistoryTagFilter)
                continue;
            if (expanded || (bounds.Y < scroll.VerticalOffset + scroll.ViewportHeight - 0.5
                && bounds.Bottom > scroll.VerticalOffset + 0.5))
                candidates.Add(child);
        }
        return candidates;
    }

    internal static void Navigate(ItemsControl items, ScrollViewer scroll, HistoryViewModel model, bool expanded, bool forward)
    {
        var candidates = GetCandidates(items, scroll, expanded);
        var target = model.SelectAdjacentTag(candidates.Select(child => (HistoryTagFilter)child.DataContext).ToArray(), forward);
        if (target is not null && expanded)
            candidates.First(child => ReferenceEquals(child.DataContext, target)).StartBringIntoView();
    }
}
