using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.Models;

public record class UniqueCommand(string Name, string CmdId, Action Command, Hotkey? Hotkey = null);