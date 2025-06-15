using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.ViewModels;

public record UniqueCommandViewModel(string Name, Guid Guid, bool IsError, Hotkey Hotkey);