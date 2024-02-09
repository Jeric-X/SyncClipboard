using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.WinUI3.Win32;
using System.Collections.Generic;
using Windows.System;
using static Vanara.PInvoke.User32;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HotkeyInput : UserControl
{
    private readonly HashSet<VirtualKey> _pressedKeys = new();
    private readonly HashSet<VirtualKey> _pressingKeys = new();

    public HotkeyInput()
    {
        this.InitializeComponent();
    }

    public Hotkey Hotkey
    {
        get { return (Hotkey)_HotkeyViewer.GetValue(HotkeyViewer.HotkeyProperty); }
        set { _HotkeyViewer.SetValue(HotkeyViewer.HotkeyProperty, value); }
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (_pressingKeys.Count == 0)
        {
            _pressedKeys.Clear();
        }

        _pressedKeys.Add(e.Key);
        _pressingKeys.Add(e.Key);
        OnPressedKeyChanged();
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyRoutedEventArgs e)
    {
        _pressingKeys.Remove(e.Key);
        e.Handled = true;
    }

    private void OnPressedKeyChanged()
    {
        var keys = new List<Key>();
        foreach (var virtualKey in _pressedKeys)
        {
            if (KeyboardMap.VirtualKeyMap.TryGetValue((VK)virtualKey, out Key key))
            {
                keys.Add(key);
            }
        }
        Hotkey = new Hotkey(keys);
    }
}
