namespace SyncClipboard.Core.Models.Keyboard;

public record class HotkeyStatus
{
    public Hotkey? Hotkey { get; set; } = null;
    public bool IsReady { get; set; } = false;
    public UniqueCommand? Command { get; set; } = null;
    public HotkeyStatus(Hotkey? hotkey, bool isReady = false, UniqueCommand? uniqueCommand = null)
    {
        Hotkey = hotkey;
        IsReady = isReady;
        Command = uniqueCommand;
    }
}
