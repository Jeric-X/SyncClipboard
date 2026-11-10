namespace SyncClipboard.Core.Utilities.History;

internal static class HistoryTagOrdering
{
    internal static string[] Order(IEnumerable<string> knownNames, IEnumerable<string> pinnedNames)
    {
        var pinned = pinnedNames.Distinct(StringComparer.Ordinal).ToArray();
        return [.. pinned, .. knownNames.Except(pinned, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }
}
