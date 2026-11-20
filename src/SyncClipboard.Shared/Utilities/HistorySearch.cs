namespace SyncClipboard.Shared.Utilities;

public interface IHistorySearchable
{
    string Text { get; }
    string? Title { get; }
}

public static class HistorySearch
{
    public static IQueryable<T> Apply<T>(IQueryable<T> records, string? term, bool searchTitles) where T : class, IHistorySearchable
    {
        if (string.IsNullOrEmpty(term))
            return records;

        var titleTerm = searchTitles ? term.Normalize() : term;
        var foldedTerm = ToAsciiLower(term);
        var foldedTitleTerm = titleTerm == term ? foldedTerm : ToAsciiLower(titleTerm);
#pragma warning disable CA1862 // SQLite translates these calls to lower and instr. The StringComparison overload has no translation.
        return records.Where(r => r.Text.ToLower().Contains(foldedTerm)
            || (searchTitles && r.Title != null && r.Title.ToLower().Contains(foldedTitleTerm)));
#pragma warning restore CA1862
    }

    private static string ToAsciiLower(string value)
    {
        if (!value.AsSpan().ContainsAnyInRange('A', 'Z'))
            return value;

        return string.Create(value.Length, value, static (buffer, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                buffer[i] = char.IsAsciiLetterUpper(source[i]) ? char.ToLowerInvariant(source[i]) : source[i];
        });
    }

    public static bool Contains(string value, string term)
        => term.Length <= value.Length && ToAsciiLower(value).Contains(ToAsciiLower(term), StringComparison.Ordinal);
}
