using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.WinUI3.Win32;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HotkeyInput : UserControl
{
    private readonly HashSet<VK> _pressedKeys = new();
    private readonly HashSet<VK> _pressingKeys = new();
    private SafeHHOOK? _keyboardHook;
    // 不能直接把方法名作为delegate传入native调用，会被GC回收
    private readonly HookProc _hookProc;

    public HotkeyInput()
    {
        this.InitializeComponent();
        _hookProc = HookProc;
    }

    public bool IsError
    {
        get { return (bool)_HotkeyViewer.GetValue(HotkeyViewer.IsErrorProperty); }
        set { _HotkeyViewer.SetValue(HotkeyViewer.IsErrorProperty, value); }
    }

    public Hotkey Hotkey
    {
        get { return (Hotkey)GetValue(HotkeyProperty); }
        set { if (Hotkey != value) SetValue(HotkeyProperty, value); }
    }

    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
            nameof(Hotkey),
            typeof(Hotkey),
            typeof(HotkeyInput),
            new PropertyMetadata(Hotkey.Nothing)
    );

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var hookPara = (KBDLLHOOKSTRUCT?)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
        if (nCode < 0 || !hookPara.HasValue)
        {
            return User32.CallNextHookEx(HHOOK.NULL, nCode, wParam, lParam);
        }

        var messageType = (WindowMessage)wParam.ToInt64();
        switch (messageType)
        {
            case WindowMessage.WM_KEYDOWN:
            case WindowMessage.WM_SYSKEYDOWN:
                OnKeyDown((VK)hookPara.Value.vkCode);
                break;
            case WindowMessage.WM_KEYUP:
            case WindowMessage.WM_SYSKEYUP:
                OnKeyUp((VK)hookPara.Value.vkCode);
                break;
            default:
                return User32.CallNextHookEx(HHOOK.NULL, nCode, wParam, lParam);
        }

        return new IntPtr(1);
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        _keyboardHook?.Dispose();
        _keyboardHook = User32.SetWindowsHookEx(HookType.WH_KEYBOARD_LL, _hookProc);
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        _keyboardHook?.Dispose();
        _keyboardHook = null;
        _pressingKeys.Clear();
        base.OnLostFocus(e);
    }

    private void OnKeyUp(VK key)
    {
        _pressingKeys.Remove(key);
    }

    private void OnKeyDown(VK key)
    {
        if (_pressingKeys.Count == 0)
        {
            _pressedKeys.Clear();
        }

        if (KeyboardMap.VirtualKeyMap.ContainsKey(key))
        {
            _pressedKeys.Add(key);
            _pressingKeys.Add(key);
            UpdateHotkeyViewer();
        }
    }

    private void UpdateHotkeyViewer()
    {
        var keys = new List<Key>();
        foreach (var virtualKey in _pressedKeys)
        {
            if (KeyboardMap.VirtualKeyMap.TryGetValue(virtualKey, out Key key))
            {
                keys.Add(key);
            }
        }
        Hotkey = new Hotkey(keys);
    }
}
