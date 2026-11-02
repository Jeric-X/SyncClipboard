using System.Text;

namespace SyncClipboard.Shared.Utilities;

public static class HistoryTags
{
    public const int MaxNameLength = 64;
    public const int MaxCount = 32;

    public static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = name.Normalize(NormalizationForm.FormC).Trim();
        if (normalized.Length == 0 || normalized.EnumerateRunes().Count() > MaxNameLength
            || normalized.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))
        {
            throw new ArgumentException($"Tag names must contain 1–{MaxNameLength} characters without line breaks or control characters.");
        }
        return normalized;
    }

    public static string[] NormalizeNames(IEnumerable<string> tags) =>
        tags.Select(NormalizeName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static string[] Normalize(IEnumerable<string> tags)
    {
        var result = NormalizeNames(tags);
        if (result.Length > MaxCount)
            throw new ArgumentException($"A record can have at most {MaxCount} tags.");
        return result;
    }

    public static string[] Apply(IEnumerable<string> current, IEnumerable<string> added, IEnumerable<string> removed)
    {
        var additions = NormalizeNames(added);
        var removals = NormalizeNames(removed);
        return Normalize(NormalizeNames(current).Except(removals, StringComparer.Ordinal).Concat(additions));
    }
}
