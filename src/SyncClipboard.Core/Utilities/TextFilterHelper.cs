using SyncClipboard.Core.Models.UserConfigs;
using System.Text.RegularExpressions;

namespace SyncClipboard.Core.Utilities;

internal static class TextFilterHelper
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static async Task<bool> AllowsUploadAsync(
        TextProfile profile, TextFilterConfig config, CancellationToken token, string? clipboardText = null)
    {
        var isBlackList = config.FilterMode == "BlackList";
        if (!isBlackList && config.FilterMode != "WhiteList")
            return true;

        var rules = isBlackList ? config.BlackList : config.WhiteList;
        if (rules.Count == 0)
            return isBlackList;

        var text = clipboardText ?? await profile.ReadFullText(token);
        var matched = rules.Any(rule => Matches(rule, text, onError: isBlackList));
        return isBlackList ? !matched : matched;
    }

    public static bool ExcludesFromHistory(string text, TextFilterConfig config) => config.FilterMode == "BlackList"
        && config.BlackList.Any(rule => rule.ExcludeFromHistory && Matches(rule, text, onError: true));

    public static string? ValidateRule(TextFilterRule rule)
    {
        if (string.IsNullOrEmpty(rule.Pattern))
            return I18n.Strings.FileFilterPatternRequired;

        try
        {
            if (rule.UseRegex)
                _ = new Regex(rule.Pattern, RegexOptions.CultureInvariant, RegexTimeout);
            return null;
        }
        catch (ArgumentException error)
        {
            return string.Format(I18n.Strings.InvalidRegularExpression, error.Message);
        }
    }

    private static bool Matches(TextFilterRule rule, string text, bool onError)
    {
        if (string.IsNullOrEmpty(rule.Pattern))
            return false;
        if (!rule.UseRegex)
            return text.Contains(rule.Pattern, StringComparison.Ordinal);

        try
        {
            return Regex.IsMatch(text, rule.Pattern, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return onError;
        }
    }
}
