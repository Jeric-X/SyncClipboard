namespace SyncClipboard.Shared.Utilities;

public static class HistoryTitles
{
    public const int MaxLength = 256;

    public static string? Normalize(string? title)
    {
        if (title is null)
            return null;

        string normalized;
        try
        {
            normalized = title.Normalize().Trim();
        }
        catch (ArgumentException)
        {
            throw new HistoryTitleValidationException(HistoryTitleError.InvalidCharacter);
        }
        if (normalized.EnumerateRunes().Count() > MaxLength)
            throw new HistoryTitleValidationException(HistoryTitleError.TooLong);
        if (normalized.Any(c => c is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029'))
            throw new HistoryTitleValidationException(HistoryTitleError.LineBreak);
        if (normalized.Any(char.IsControl))
            throw new HistoryTitleValidationException(HistoryTitleError.ControlCharacter);
        return normalized;
    }
}
