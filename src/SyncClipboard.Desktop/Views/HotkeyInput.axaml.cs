using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Desktop.Utilities;
using System.Collections.Generic;
using Key = SyncClipboard.Core.Models.Keyboard.Key;
using AvaloniaKey = Avalonia.Input.Key;

namespace SyncClipboard.Desktop.Views;

public partial class HotkeyInput : UserControl
{
    private readonly HashSet<AvaloniaKey> _pressedKeys = [];
    private readonly HashSet<AvaloniaKey> _pressingKeys = [];

    public HotkeyInput()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
    }

    public bool IsError
    {
        get { return GetValue(IsErrorProperty); }
        set { SetValue(IsErrorProperty, value); }
    }

    public static readonly StyledProperty<bool> IsErrorProperty = AvaloniaProperty.Register<HotkeyInput, bool>(
        nameof(IsError), false
    );

    public Hotkey Hotkey
    {
        get { return GetValue(HotkeyProperty); }
        set { if (Hotkey != value) SetValue(HotkeyProperty, value); }
    }

    public static readonly StyledProperty<Hotkey> HotkeyProperty = AvaloniaProperty.Register<HotkeyInput, Hotkey>(
        nameof(Hotkey), Hotkey.Nothing
    );

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        _pressingKeys.Clear();
        base.OnLostFocus(e);
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        _pressingKeys.Remove(e.Key);
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_pressingKeys.Count == 0)
        {
            _pressedKeys.Clear();
        }

        if (KeyboardMap.AvaloniaKeyMap.ContainsKey(e.Key))
        {
            _pressedKeys.Add(e.Key);
            _pressingKeys.Add(e.Key);
            UpdateHotkeyViewer();
        }
        e.Handled = true;
    }

    private void UpdateHotkeyViewer()
    {
        var keys = new List<Key>();
        foreach (var avaloniaKey in _pressedKeys)
        {
            if (KeyboardMap.AvaloniaKeyMap.TryGetValue(avaloniaKey, out Key key))
            {
                keys.Add(key);
            }
        }
        Hotkey = new Hotkey(keys);
    }
}
