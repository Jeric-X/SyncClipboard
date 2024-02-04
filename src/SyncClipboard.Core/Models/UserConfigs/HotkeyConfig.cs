namespace SyncClipboard.Core.Models.UserConfigs;

public record HotkeyConfig
{
    public Dictionary<Guid, Hotkey> Hotkeys { get; set; } = new();
}
