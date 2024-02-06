using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using System;

namespace SyncClipboard.Desktop.Utilities;

internal class NativeHotkeyRegistryStub : INativeHotkeyRegistry
{
    public bool RegisterForSystemHotkey(Hotkey hotkey, Action action)
    {
        return true;
    }

    public void UnRegisterForSystemHotkey(Hotkey hotkey)
    {
    }
}
