using SyncClipboard.Shared.Attributes;
using SyncClipboard.Shared.Interfaces;
using System.Text.RegularExpressions;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record TextFilterConfig : IConfigValidator
{
    public const string ConfigKey = "TextFilter";
    public string FilterMode { get; init; } = "";
    public List<TextFilterRule> BlackList { get; init; } = [];
    public List<TextFilterRule> WhiteList { get; init; } = [];

    public void Validate()
    {
        if (FilterMode is not ("" or "BlackList" or "WhiteList"))
            throw new InvalidDataException($"Unsupported text filter mode '{FilterMode}'.");

        foreach (var rule in BlackList.Concat(WhiteList))
        {
            if (rule.Validate() is { } error)
                throw new InvalidDataException(error);
        }
    }

    public virtual bool Equals(TextFilterConfig? other) => other is not null && FilterMode == other.FilterMode
        && BlackList.SequenceEqual(other.BlackList) && WhiteList.SequenceEqual(other.WhiteList);

    public override int GetHashCode() => HashCode.Combine(FilterMode, BlackList.ListHashCode(), WhiteList.ListHashCode());

    public bool AllowsUpload(string text) => FilterMode switch
    {
        "BlackList" => !BlackList.Any(rule => rule.Matches(text, onError: true)),
        "WhiteList" => WhiteList.Any(rule => rule.Matches(text, onError: false)),
        _ => true,
    };

    public bool ExcludesFromHistory(string text) => FilterMode == "BlackList"
        && BlackList.Any(rule => rule.ExcludeFromHistory && rule.Matches(text, onError: true));
}

public record TextFilterRule(string Pattern, bool UseRegex = false, bool ExcludeFromHistory = false)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    public string? Validate()
    {
        if (string.IsNullOrEmpty(Pattern))
            return I18n.Strings.FileFilterPatternRequired;

        try
        {
            if (UseRegex)
                _ = new Regex(Pattern, RegexOptions.CultureInvariant, Timeout);
            return null;
        }
        catch (ArgumentException error)
        {
            return string.Format(I18n.Strings.InvalidRegularExpression, error.Message);
        }
    }

    internal bool Matches(string text, bool onError)
    {
        if (string.IsNullOrEmpty(Pattern))
            return false;
        if (!UseRegex)
            return text.Contains(Pattern, StringComparison.Ordinal);

        try
        {
            return Regex.IsMatch(text, Pattern, RegexOptions.CultureInvariant, Timeout);
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return onError;
        }
    }
}
