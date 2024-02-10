using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.Interfaces;

public interface INativeHotkeyRegistry
{
    bool RegisterForSystemHotkey(Hotkey hotkey, Action action);
    void UnRegisterForSystemHotkey(Hotkey hotkey);
    bool IsValidHotkeyForm(Hotkey hotkey);
}