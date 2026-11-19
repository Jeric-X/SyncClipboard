namespace SyncClipboard.Shared.Utilities;

public static class HistoryTitles
{
    public const int MaxLength = 256;

    public static string? Normalize(string? title)
    {
        if (title is null)
            return null;

        var normalized = title.Normalize().Trim();
        if (normalized.EnumerateRunes().Count() > MaxLength
            || normalized.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))
        {
            throw new ArgumentException($"Titles must contain at most {MaxLength} characters without line breaks or control characters.");
        }
        return normalized;
    }
}
