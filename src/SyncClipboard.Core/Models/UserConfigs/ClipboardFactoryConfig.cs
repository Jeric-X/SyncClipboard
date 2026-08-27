using SyncClipboard.Core.Utilities;
using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record class ClipboardFactoryConfig
{
    public const string ConfigKey = "ClipboardFactory";

    public List<string> ProhibitSources { get; set; } = [];

    public virtual bool Equals(ClipboardFactoryConfig? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        if (ProhibitSources.Count != other.ProhibitSources.Count) return false;
        if (ProhibitSources.Except(other.ProhibitSources).Any()) return false;

        return true;
    }

    public override int GetHashCode()
    {
        return ProhibitSources.ListHashCode();
    }
}
