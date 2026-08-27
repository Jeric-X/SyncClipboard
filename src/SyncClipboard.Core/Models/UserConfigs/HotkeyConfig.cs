using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record HotkeyConfig
{
    public const string ConfigKey = "Hotkey";

    public Dictionary<string, Hotkey> Hotkeys { get; set; } = [];

    public virtual bool Equals(HotkeyConfig? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        if (ReferenceEquals(Hotkeys, other.Hotkeys)) return true;

        return Hotkeys.Count == other.Hotkeys.Count && !Hotkeys.Except(other.Hotkeys).Any();
    }

    public override int GetHashCode()
    {
        return Hotkeys.ListHashCode();
    }
}
