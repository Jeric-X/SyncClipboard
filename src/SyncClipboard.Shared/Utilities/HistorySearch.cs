using Microsoft.EntityFrameworkCore;
using System.Text;

namespace SyncClipboard.Shared.Utilities;

public interface IHistorySearchable
{
    string Text { get; }
    string? Title { get; }
}

public static class HistorySearch
{
    private const string Escape = "!";
    private const int MaxLikePatternBytes = 50_000;

    public static IQueryable<T> Apply<T>(IQueryable<T> records, string? term, bool searchTitles) where T : class, IHistorySearchable
    {
        if (string.IsNullOrEmpty(term))
            return records;

        var titleTerm = searchTitles ? term.Normalize() : term;
        var pattern = ToLikePattern(term);
        var titlePattern = titleTerm == term ? pattern : ToLikePattern(titleTerm);
        if (!term.Contains('\0') && FitsLikePattern(pattern) && FitsLikePattern(titlePattern))
        {
            return records.Where(r => EF.Functions.Like(r.Text, pattern, Escape)
                || (searchTitles && EF.Functions.Like(r.Title, titlePattern, Escape)));
        }

        // SQLite LIKE is limited in UTF-8 bytes and stops at NUL. Use native lower/instr for these terms.
        var foldedTerm = ToAsciiLower(term);
        var foldedTitleTerm = titleTerm == term ? foldedTerm : ToAsciiLower(titleTerm);
#pragma warning disable CA1862 // SQLite translates ToLower and Contains to lower and instr. The StringComparison overload has no translation.
        return records.Where(r => r.Text.ToLower().Contains(foldedTerm)
            || (searchTitles && r.Title != null && r.Title.ToLower().Contains(foldedTitleTerm)));
#pragma warning restore CA1862
    }

    private static string ToLikePattern(string term)
        => "%" + term.Replace(Escape, "!!").Replace("%", "!%").Replace("_", "!_") + "%";

    private static bool FitsLikePattern(string pattern) => Encoding.UTF8.GetByteCount(pattern) <= MaxLikePatternBytes;

    private static string ToAsciiLower(string value) => string.Create(value.Length, value, static (buffer, source) =>
    {
        for (var i = 0; i < source.Length; i++)
            buffer[i] = char.IsAsciiLetterUpper(source[i]) ? char.ToLowerInvariant(source[i]) : source[i];
    });

    public static bool Contains(string value, string term)
    {
        var remaining = value.AsSpan();
        int index;
        while ((index = remaining.IndexOf(term, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var match = remaining.Slice(index, term.Length);
            if (HasSameNonAsciiCharacters(match, term))
                return true;
            remaining = remaining[(index + 1)..];
        }
        return false;
    }

    // SQLite LIKE folds ASCII letters only. Other characters in a candidate match must be identical.
    private static bool HasSameNonAsciiCharacters(ReadOnlySpan<char> match, string term)
    {
        for (var i = 0; i < term.Length; i++)
        {
            if (match[i] != term[i] && (!char.IsAscii(match[i]) || !char.IsAscii(term[i])))
                return false;
        }
        return true;
    }
}
