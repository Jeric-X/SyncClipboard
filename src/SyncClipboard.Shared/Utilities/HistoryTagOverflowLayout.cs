namespace SyncClipboard.Shared.Utilities;

public readonly record struct HistoryTagSize(double Width, double Height);
public readonly record struct HistoryTagPlacement(double X, double Y, double Width, double Height);

public sealed record HistoryTagOverflowLayout(HistoryTagPlacement[] Tags, HistoryTagPlacement? More, double Height)
{
    public static HistoryTagOverflowLayout Calculate(
        IEnumerable<HistoryTagSize> tags, HistoryTagSize more, double width, double height, double lineSpacing = 0)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        var placements = new List<HistoryTagPlacement>();
        double x = 0, y = 0, rowHeight = 0;
        var overflowed = false;
        foreach (var tag in tags)
        {
            if (!TryPlace(tag, width, height, lineSpacing, ref x, ref y, ref rowHeight, out var placement))
            {
                overflowed = true;
                break;
            }
            placements.Add(placement);
        }
        if (!overflowed)
            return AlignRows(placements, null, width, y + rowHeight);

        // Reserve room for the overflow chip, removing the last visible tags as needed.
        while (true)
        {
            x = y = rowHeight = 0;
            foreach (var placement in placements)
                TryPlace(new(placement.Width, placement.Height), width, height, lineSpacing, ref x, ref y, ref rowHeight, out _);
            if (TryPlace(more, width, height, lineSpacing, ref x, ref y, ref rowHeight, out var overflow))
                return AlignRows(placements, overflow, width, y + rowHeight);
            if (placements.Count == 0)
                return new([], null, 0);
            placements.RemoveAt(placements.Count - 1);
        }
    }

    private static HistoryTagOverflowLayout AlignRows(
        List<HistoryTagPlacement> tags, HistoryTagPlacement? more, double width, double height)
    {
        var rows = tags.Concat(more is { } overflow ? [overflow] : [])
            .GroupBy(p => p.Y).ToDictionary(row => row.Key,
                row => (Height: row.Max(p => p.Height), Right: row.Max(p => p.X + p.Width)));
        HistoryTagPlacement Align(HistoryTagPlacement p) => p with
        {
            X = p.X + Math.Max(0, width - rows[p.Y].Right),
            Y = p.Y + (rows[p.Y].Height - p.Height) / 2
        };
        return new(tags.Select(Align).ToArray(), more is { } marker ? Align(marker) : null, height);
    }

    private static bool TryPlace(HistoryTagSize size, double width, double height, double lineSpacing,
        ref double x, ref double y, ref double rowHeight, out HistoryTagPlacement placement)
    {
        placement = default;
        if (width <= 0 || height <= 0)
            return false;
        var itemWidth = Math.Min(size.Width, width);
        if (x > 0 && x + itemWidth > width)
        {
            y += rowHeight + lineSpacing;
            x = rowHeight = 0;
        }
        if (y + size.Height > height)
            return false;
        placement = new(x, y, itemWidth, size.Height);
        x += itemWidth;
        rowHeight = Math.Max(rowHeight, size.Height);
        return true;
    }
}
