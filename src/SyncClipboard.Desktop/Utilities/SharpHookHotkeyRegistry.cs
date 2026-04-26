using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Utilities.Keyboard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Utilities;

internal partial class SharpHookHotkeyRegistry : INativeHotkeyRegistry, IDisposable
{
    private readonly IGlobalHook _globalHook;
    private readonly Dictionary<KeyCode, DateTime> _pressingKeys = [];
    private readonly Dictionary<Hotkey, Action> _registedHotkeys = [];
    private static readonly TimeSpan KeyPressTimeout = TimeSpan.FromSeconds(30);

    private readonly AutoResetEvent _globalHookRunEvent = new(false);

    [SupportedOSPlatform("macos")]
    [LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AXIsProcessTrusted();
    private bool _newPermissionApplied = false;

    public bool SupressHotkey { get; set; } = false;

    public SharpHookHotkeyRegistry(IGlobalHook globalHook)
    {
        _globalHook = globalHook;
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
        return _globalHook.IsRunning && _registedHotkeys.TryAdd(hotkey, action);
    }

    public void UnRegisterForSystemHotkey(Hotkey hotkey)
    {
        _registedHotkeys.Remove(hotkey);
    }

    private void CheckForMacPermission()
    {
        if (OperatingSystem.IsMacOS() && AXIsProcessTrusted() is false)
        {
            if (_newPermissionApplied)
                return;

            _newPermissionApplied = true;
            try
            {
                Process.Start("tccutil", "reset Accessibility xyz.jericx.desktop.syncclipboard").WaitForExit(1000);
            }
            catch (Exception ex)
            {
                App.Current.Logger.Write(ex.Message);
            }
        }
    }

    public void CheckGlobalHook()
    {
        if (_globalHook.IsRunning)
            return;

        lock (this)
        {
            if (_globalHook.IsRunning)
                return;

            CheckForMacPermission();
            Task.Run(_globalHook.Run).ContinueWith(task =>
            {
                _globalHookRunEvent.Set();
            }, TaskContinuationOptions.NotOnRanToCompletion);

            _globalHookRunEvent.WaitOne(TimeSpan.FromSeconds(1));
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
