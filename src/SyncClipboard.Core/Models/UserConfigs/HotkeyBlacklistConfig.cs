using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record HotkeyBlacklistConfig
{
    public const string ConfigKey = "HotkeyBlacklist";

    public bool Enabled { get; set; }
    public List<ForegroundWindowInfo> BlackList { get; set; } = [];

    public virtual bool Equals(HotkeyBlacklistConfig? other)
    {
        return other is not null
            && Enabled == other.Enabled
            && BlackList.SequenceEqual(other.BlackList);
    }

    public override int GetHashCode()
    {
        var hash = Enabled.GetHashCode();
        foreach (var item in BlackList)
        {
            hash = HashCode.Combine(hash, item.ProcessName, item.WindowTitle, item.ExecutableName);
        }
        return hash;
    }
}
