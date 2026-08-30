using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.Commons;

public sealed class ForegroundWindowCapture(
    INativeHotkeyRegistry nativeHotkeyRegistry,
    INativeWindowController foregroundWindowInfoProvider)
{
    private readonly object _syncRoot = new();
    private Hotkey? _registeredHotkey;

    public event Action<WindowInfo>? WindowCaptured;

    public bool TryStart(out Hotkey? hotkey)
    {
        lock (_syncRoot)
        {
            Stop();
            for (var key = Key.A; key <= Key.Z; key++)
            {
                var candidate = new Hotkey(Key.Ctrl, Key.Shift, Key.Alt, key);
                if (nativeHotkeyRegistry.RegisterForSystemHotkey(candidate, CaptureForegroundWindow))
                {
                    _registeredHotkey = candidate;
                    hotkey = candidate;
                    return true;
                }
            }
        }

        hotkey = null;
        return false;
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            if (_registeredHotkey is not null)
            {
                nativeHotkeyRegistry.UnRegisterForSystemHotkey(_registeredHotkey);
                _registeredHotkey = null;
            }
        }
    }

    private void CaptureForegroundWindow()
    {
        var window = foregroundWindowInfoProvider.GetForegroundWindowInfo() ?? new WindowInfo();
        Stop();
        WindowCaptured?.Invoke(window);
    }
}
