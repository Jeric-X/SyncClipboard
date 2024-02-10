using Microsoft.UI.Xaml;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinUIEx;
using WinUIEx.Messaging;
using static Vanara.PInvoke.User32;

namespace SyncClipboard.WinUI3.Win32;

public class NativeHotkeyRegistry : INativeHotkeyRegistry, IDisposable
{
    private readonly IntPtr _handle;
    private readonly WindowMessageMonitor _messageMonitor;

    private int _hotkeyId = 0;
    private readonly Dictionary<int, Action> _hotkeyList = new();
    private readonly Dictionary<Hotkey, int> _hotkeyIdMap = new();

    public NativeHotkeyRegistry(Window window)
    {
        _handle = window.GetWindowHandle();
        _messageMonitor = new WindowMessageMonitor(_handle);
        _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;
    }

    private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message.MessageId == (uint)WindowMessage.WM_HOTKEY)
        {
            bool found = _hotkeyList.TryGetValue((int)e.Message.WParam, out Action? action);
            if (found)
            {
                action?.Invoke();
                e.Handled = true;
            }
        }
    }

    public void UnRegisterForSystemHotkey(Hotkey hotkey)
    {
        var found = _hotkeyIdMap.TryGetValue(hotkey, out int id);
        if (found)
        {
            UnregisterHotKey(_handle, id);
            lock (_hotkeyList)
            {
                _hotkeyList.Remove(id);
                _hotkeyIdMap.Remove(hotkey);
            }
        }
    }

    private static HotKeyModifiers GetKeyModifiers(Hotkey hotkey)
    {
        HotKeyModifiers modifiers = HotKeyModifiers.MOD_NOREPEAT;
        foreach (var key in hotkey.Keys)
        {
            if (KeyboardMap.ModifierMap.TryGetValue(key, out var modifier))
            {
                modifiers |= modifier;
            }
        }
        return modifiers;
    }

    private static uint GetVirtualKey(Hotkey hotkey)
    {
        var virtualKeys = hotkey.Keys.Where(key => !KeyboardMap.ModifierMap.ContainsKey(key));
        var count = virtualKeys.Count();
        if (count > 1)
        {
            throw new ArgumentException("Not support multiple normal keys");
        }
        else if (count == 0)
        {
            throw new ArgumentException("No key found");
        }
        if (!KeyboardMap.VirtualKeyMapReverse.TryGetValue(virtualKeys.First(), out VK virtualKey))
        {
            throw new ArgumentException($"Not support key {virtualKeys.First()}");
        }

        return (uint)virtualKey;
    }

    public bool RegisterForSystemHotkey(Hotkey hotkey, Action action)
    {
        var id = Interlocked.Increment(ref _hotkeyId);

        bool res = RegisterHotKey(_handle, id, GetKeyModifiers(hotkey), GetVirtualKey(hotkey));
        if (res is false)
        {
            return false;
        }

        lock (_hotkeyList)
        {
            _hotkeyIdMap.Add(hotkey, id);
            _hotkeyList.Add(id, action);
        }
        return true;
    }

    public bool IsValidHotkeyForm(Hotkey hotkey)
    {
        try
        {
            GetVirtualKey(hotkey);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    ~NativeHotkeyRegistry() => Dispose();

    public void Dispose()
    {
        lock (_hotkeyList)
        {
            foreach (var id in _hotkeyList.Keys)
            {
                UnregisterHotKey(_handle, id);
            }
            _hotkeyList.Clear();
            _hotkeyIdMap.Clear();
        }
        _messageMonitor.WindowMessageReceived -= OnWindowMessageReceived;
        _messageMonitor.Dispose();
        System.GC.SuppressFinalize(this);
    }
}
