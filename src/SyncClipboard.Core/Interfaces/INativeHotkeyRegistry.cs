using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface INativeHotkeyRegistry
{
    bool RegisterForSystemHotkey(Hotkey hotkey, Action action);
    void UnRegisterForSystemHotkey(Hotkey hotkey);
}