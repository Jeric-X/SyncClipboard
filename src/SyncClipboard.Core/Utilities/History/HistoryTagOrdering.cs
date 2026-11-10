using SyncClipboard.Core.ViewModels.Sub;

namespace SyncClipboard.Core.Utilities.History;

public static class HistoryTagOrdering
{
    public static HistoryPreviewTag[] OrderForDisplay(IEnumerable<HistoryPreviewTag>? tags, IEnumerable<string>? pinnedNames)
    {
        var items = tags?.ToArray() ?? [];
        var names = items.Select(tag => tag.Name).ToArray();
        var pinned = (pinnedNames ?? []).Intersect(names, StringComparer.Ordinal);
        return Order(names, pinned).Select(name => items.First(tag => tag.Name == name)).ToArray();
    }

    internal static string[] Order(IEnumerable<string> knownNames, IEnumerable<string> pinnedNames)
    {
        var pinned = pinnedNames.Distinct(StringComparer.Ordinal).ToArray();
        return [.. pinned, .. knownNames.Except(pinned, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }
}
