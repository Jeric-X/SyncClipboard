using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.ViewModels;

public record UniqueCommandViewModel(string Name, string CmdId, bool IsError, Hotkey Hotkey);