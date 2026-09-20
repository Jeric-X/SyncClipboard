using Avalonia.Threading;
using SharpHook;
using SharpHook.Data;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Utilities.Keyboard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SyncClipboard.Desktop.Utilities;

internal partial class SharpHookHotkeyRegistry : INativeHotkeyRegistry, IDisposable
{
    private readonly IGlobalHook _globalHook;
    private readonly ILogger _logger;
    private readonly Dictionary<KeyCode, DateTime> _pressingKeys = [];
    private readonly Dictionary<Hotkey, Action> _registedHotkeys = [];
    private static readonly TimeSpan KeyPressTimeout = TimeSpan.FromSeconds(30);

    private readonly AutoResetEvent _globalHookRunEvent = new(false);

    public bool SupressHotkey { get; set; } = false;

    public SharpHookHotkeyRegistry(IGlobalHook globalHook, ILogger logger)
    {
        _globalHook = globalHook;
        _logger = logger;
        _globalHook.KeyPressed += KeyPressed;
        _globalHook.KeyReleased += KeyReleased;
        _globalHook.HookEnabled += HookEnabled;
    }

    private void HookEnabled(object? sender, HookEventArgs e)
    {
        _globalHookRunEvent.Set();
    }

    private void KeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        _pressingKeys.Remove(e.Data.KeyCode);
    }

    private void CleanupExpiredKeys()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _pressingKeys
            .Where(kvp => now - kvp.Value > KeyPressTimeout)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in expiredKeys)
        {
            _pressingKeys.Remove(key);
        }
    }

    private void KeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (SupressHotkey)
        {
            return;
        }
        CleanupExpiredKeys();
        if (_pressingKeys.ContainsKey(e.Data.KeyCode))
        {
            return;
        }
        _pressingKeys[e.Data.KeyCode] = DateTime.UtcNow;

        if (_registedHotkeys.TryGetValue(CreateHotkey(), out var action))
        {
            e.SuppressEvent = true;
            Dispatcher.UIThread.Invoke(action);
        }
    }

    private Hotkey CreateHotkey()
    {
        var keys = _pressingKeys.Keys
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
        return _globalHook.IsRunning && _registedHotkeys.TryAdd(NormalizeHotkey(hotkey), action);
    }

    public void UnRegisterForSystemHotkey(Hotkey hotkey)
    {
        _registedHotkeys.Remove(NormalizeHotkey(hotkey));
    }

    private static Hotkey NormalizeHotkey(Hotkey hotkey) => new(hotkey.Keys.Select(key => key switch
    {
        Key.Kanji => Key.Hanja,
        Key.Hangul => Key.Kana,
        _ => key
    }));

    public void CheckGlobalHook()
    {
        if (_globalHook.IsRunning)
            return;

        lock (this)
        {
            if (_globalHook.IsRunning)
                return;

            _globalHookRunEvent.Reset();
            _globalHook.RunAsync(GlobalHookType.Keyboard, useBackgroundThread: true).ContinueWith(task =>
            {
                if (task.Exception is not null)
                {
                    _logger.Write(nameof(SharpHookHotkeyRegistry), task.Exception.GetBaseException().ToString());
                }
                _globalHookRunEvent.Set();
            });

            _globalHookRunEvent.WaitOne(TimeSpan.FromSeconds(1));
        }
    }

    ~SharpHookHotkeyRegistry() => Dispose();

    public void Dispose()
    {
        _globalHook.KeyPressed -= KeyPressed;
        _globalHook.KeyReleased -= KeyReleased;
        _globalHook.HookEnabled -= HookEnabled;
        GC.SuppressFinalize(this);
    }
}
