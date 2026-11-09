using Avalonia;
using Avalonia.Controls;
using SyncClipboard.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SyncClipboard.Desktop.Utilities;

internal sealed class HistoryTagNavigation
{
    private (Control Element, Rect Bounds, Size Viewport)? _selectionLayout;
    private double _wheelDelta;
    private double _lastSnappedOffset;

    internal void KeepSelectionVisible(ItemsControl items, ScrollViewer scroll)
    {
        var selected = GetCandidates(items).FirstOrDefault(child =>
            child.DataContext is HistoryTagFilter { IsSelected: true });
        if (selected is null)
        {
            _selectionLayout = null;
            return;
        }

        var layout = (selected, selected.Bounds, scroll.Viewport);
        if (_selectionLayout == layout)
            return;

        // Scroll offsets are deliberately excluded so manual scrolling does not snap back.
        _selectionLayout = layout;
        BringIntoView(items, scroll, selected);
    }

    internal void ScrollByWheel(ItemsControl items, ScrollViewer scroll, double delta)
    {
        if (Math.Sign(delta) != Math.Sign(_wheelDelta))
            _wheelDelta = 0;
        _wheelDelta += delta;
        var steps = (int)_wheelDelta;
        _wheelDelta -= steps;
        if (steps == 0)
            return;

        var rows = GetRowOffsets(items, scroll);
        if (rows.Length == 0)
            return;
        var current = rows.MinBy(row => Math.Abs(row - scroll.Offset.Y));
        var index = Array.IndexOf(rows, current);
        SetOffset(scroll, rows[Math.Clamp(index - steps, 0, rows.Length - 1)]);
    }

    internal void SnapToRow(ItemsControl items, ScrollViewer scroll)
    {
        var rows = GetRowOffsets(items, scroll);
        var offset = scroll.Offset.Y;
        if (rows.Length == 0)
        {
            _lastSnappedOffset = offset;
            return;
        }

        // Native scrollbar steps can be shorter than half a row; keep their direction.
        var target = offset > _lastSnappedOffset + 0.5
            ? rows.FirstOrDefault(row => row >= offset - 0.5, rows[^1])
            : offset < _lastSnappedOffset - 0.5
                ? rows.LastOrDefault(row => row <= offset + 0.5, rows[0])
                : rows.MinBy(row => Math.Abs(row - offset));
        _lastSnappedOffset = target;
        SetOffset(scroll, target);
    }

    private static double[] GetRowOffsets(ItemsControl items, ScrollViewer scroll)
    {
        var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        return GetCandidates(items).Select(child => child.Bounds.Y).Distinct()
            .Where(top => top <= maximum + 0.5).ToArray();
    }

    private static void ScrollToNearestRow(ItemsControl items, ScrollViewer scroll, double desired)
    {
        var rows = GetRowOffsets(items, scroll);
        if (rows.Length > 0)
            SetOffset(scroll, rows.MinBy(row => Math.Abs(row - desired)));
    }

    private static void SetOffset(ScrollViewer scroll, double offset)
    {
        if (Math.Abs(scroll.Offset.Y - offset) > 0.5)
            scroll.Offset = new Vector(0, offset);
    }

    private static void BringIntoView(ItemsControl items, ScrollViewer scroll, Control target)
    {
        var bounds = target.Bounds;
        var offset = scroll.Offset.Y;
        var height = scroll.Viewport.Height;
        if (bounds.Y < offset)
            offset = bounds.Y;
        else if (bounds.Bottom > offset + height)
            offset = bounds.Bottom - height;
        ScrollToNearestRow(items, scroll, offset);
    }

    internal static IReadOnlyList<Control> GetCandidates(ItemsControl items)
    {
        var candidates = new List<Control>();
        if (items.ItemsPanelRoot is not { } panel)
            return candidates;

        foreach (var child in panel.Children)
        {
            if (!child.IsVisible || child.Bounds.Height <= 0 || child.DataContext is not HistoryTagFilter)
                continue;
            candidates.Add(child);
        }
        return candidates;
    }

    internal static void Navigate(ItemsControl items, ScrollViewer scroll, HistoryViewModel model,
        bool forward, bool vertical = false)
    {
        var candidates = GetCandidates(items);
        var target = vertical
            ? model.SelectTagInAdjacentRow(candidates.Select(child =>
            {
                var bounds = child.Bounds;
                return ((HistoryTagFilter)child.DataContext!, bounds.X, bounds.Y, bounds.Width);
            }).ToArray(), forward)
            : model.SelectAdjacentTag(candidates.Select(child => (HistoryTagFilter)child.DataContext!).ToArray(), forward);
        if (target is not null)
            BringIntoView(items, scroll, candidates.First(child => ReferenceEquals(child.DataContext, target)));
    }
}
