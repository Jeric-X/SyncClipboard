using SyncClipboard.Core.Utilities;
using SyncClipboard.Shared.Attributes;
using SyncClipboard.Shared.Interfaces;

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
            if (TextFilterHelper.ValidateRule(rule) is { } error)
                throw new InvalidDataException(error);
        }
    }

    public virtual bool Equals(TextFilterConfig? other) => other is not null && FilterMode == other.FilterMode
        && BlackList.SequenceEqual(other.BlackList) && WhiteList.SequenceEqual(other.WhiteList);

    public override int GetHashCode() => HashCode.Combine(FilterMode, BlackList.ListHashCode(), WhiteList.ListHashCode());
}

public record TextFilterRule(string Pattern, bool UseRegex = false, bool ExcludeFromHistory = false);
