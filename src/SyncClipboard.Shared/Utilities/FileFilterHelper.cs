using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SyncClipboard.Shared.Models;

namespace SyncClipboard.Shared.Utilities;

public static class FileFilterHelper
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);

    public static bool IsFileAvailableAfterFilter(string fileName, FileFilterConfig filterConfig)
    {
        var baseFileName = GetFileName(fileName);

        try
        {
            if (filterConfig.FileFilterMode == "BlackList")
            {
                return !filterConfig.BlackList.Any(rule => IsMatch(fileName, baseFileName, rule));
            }

            if (filterConfig.FileFilterMode == "WhiteList")
            {
                return filterConfig.WhiteList.Any(rule => IsMatch(fileName, baseFileName, rule));
            }

            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public static bool TryValidateRule(FileFilterRule rule, out string? error)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            error = "Pattern cannot be empty.";
            return false;
        }

        if (!Enum.IsDefined(rule.MatchMode))
        {
            error = $"Unsupported match mode '{rule.MatchMode}'.";
            return false;
        }

        if (rule.MatchMode != FileFilterMatchMode.Regex)
        {
            error = null;
            return true;
        }

        try
        {
            _ = new Regex(rule.Pattern, RegexOptions.CultureInvariant, RegexTimeout);
            error = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool IsMatch(string path, string fileName, FileFilterRule rule)
    {
        return rule.MatchMode switch
        {
            FileFilterMatchMode.Suffix => path.EndsWith(rule.Pattern.Trim(), StringComparison.OrdinalIgnoreCase),
            FileFilterMatchMode.Regex => GetRegex(rule.Pattern).IsMatch(fileName),
            _ => throw new InvalidOperationException($"Unsupported file filter match mode '{rule.MatchMode}'."),
        };
    }

    private static Regex GetRegex(string pattern) => RegexCache.GetOrAdd(pattern, static value => new Regex(
        value,
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout));

    private static string GetFileName(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var separatorIndex = normalizedPath.LastIndexOf('/');
        return separatorIndex < 0 ? normalizedPath : normalizedPath[(separatorIndex + 1)..];
    }
}
