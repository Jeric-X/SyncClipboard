using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Utilities;

internal class SharpHookHotkeyRegistry : INativeHotkeyRegistry, IDisposable
{
    private readonly IGlobalHook _globalHook;
    private readonly HashSet<KeyCode> _pressingKeys = new();
    private readonly Dictionary<Hotkey, Action> _registedHotkeys = new();

    private readonly AutoResetEvent _globalHookRunEvent = new(false);
    private bool _needMasOSPermission = false;

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

    private void KeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (SupressHotkey || _pressingKeys.Contains(e.Data.KeyCode))
        {
            return;
        }
        _pressingKeys.Add(e.Data.KeyCode);

        if (_registedHotkeys.TryGetValue(CreateHotkey(), out var action))
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
        return _globalHook.IsRunning && _registedHotkeys.TryAdd(hotkey, action);
    }

    public void UnRegisterForSystemHotkey(Hotkey hotkey)
    {
        _registedHotkeys.Remove(hotkey);
    }

    private void CheckMacOSPermission()
    {
        if (OperatingSystem.IsMacOS() && _needMasOSPermission)
        {
            try
            {
                Process.Start("tccutil", "reset Accessibility xyz.jericx.desktop.syncclipboard");
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

            CheckMacOSPermission();
            Task.Run(_globalHook.Run).ContinueWith(task =>
            {
                if (task.Exception?.InnerException is HookException hookException)
                {
                    if (hookException.Result is UioHookResult.ErrorAxApiDisabled)
                    {
                        _needMasOSPermission = true;
                    }
                }
                _globalHookRunEvent.Set();
            }, TaskContinuationOptions.NotOnRanToCompletion);

            _globalHookRunEvent.WaitOne(TimeSpan.FromSeconds(0.5));
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
