using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SyncClipboard.Desktop.Utilities;

internal class SharpHookHotkeyRegistry : INativeHotkeyRegistry, IDisposable
{
    private readonly IGlobalHook _globalHook;
    private readonly HashSet<KeyCode> _pressingKeys = new();
    private readonly Dictionary<Hotkey, Action> _regestedHotkeys = new();

    public bool SupressHotkey { get; set; } = false;

    public SharpHookHotkeyRegistry(IGlobalHook globalHook)
    {
        _globalHook = globalHook;
        _globalHook.KeyPressed += KeyPressed;
        _globalHook.KeyReleased += KeyReleased;
    }

    private void KeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        _pressingKeys.Remove(e.Data.KeyCode);
    }

    private void KeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (SupressHotkey || _pressingKeys.Contains(e.Data.KeyCode))
        {
            return;
        }
        _pressingKeys.Add(e.Data.KeyCode);

        if (_regestedHotkeys.TryGetValue(CreateHotkey(), out var action))
        {
            e.SuppressEvent = true;
            Dispatcher.UIThread.Invoke(action);
        }
    }

    private Hotkey CreateHotkey()
    {
        var keys = _pressingKeys
            .Where(key => KeyCodeMap.Map.ContainsKey(key))
            .Select(key => KeyCodeMap.Map[key]);
        return new Hotkey(keys);
    }

    public bool IsValidHotkeyForm(Hotkey hotkey)
    {
        if (hotkey.Keys.Length > 5)
            return false;
        return true;
    }

    public bool RegisterForSystemHotkey(Hotkey hotkey, Action action)
    {
        CheckGlobalHook();
        return _globalHook.IsRunning && _regestedHotkeys.TryAdd(hotkey, action);
    }

    public void UnRegisterForSystemHotkey(Hotkey hotkey)
    {
        _regestedHotkeys.Remove(hotkey);
    }

    public void CheckGlobalHook()
    {
        if (_globalHook.IsRunning)
            return;
        lock (this)
        {
            if (_globalHook.IsRunning is false)
            {
                _globalHook.RunAsync();
                Thread.Sleep(500);
            }
        }
    }

    ~SharpHookHotkeyRegistry() => Dispose();

    public void Dispose()
    {
        _globalHook.KeyPressed -= KeyPressed;
        _globalHook.KeyReleased -= KeyReleased;
        GC.SuppressFinalize(this);
    }
}
