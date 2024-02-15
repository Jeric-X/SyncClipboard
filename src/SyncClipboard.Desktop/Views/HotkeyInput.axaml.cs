using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SharpHook;
using SharpHook.Native;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Desktop.Utilities;
using System.Collections.Generic;
using Key = SyncClipboard.Core.Models.Keyboard.Key;

namespace SyncClipboard.Desktop.Views;

public partial class HotkeyInput : UserControl
{
    private SharpHookHotkeyRegistry? _hotkeyRegistry;
    private readonly IGlobalHook _globalHook;
    private readonly HashSet<KeyCode> _pressedKeys = new();
    private readonly HashSet<KeyCode> _pressingKeys = new();

    public HotkeyInput()
    {
        InitializeComponent();
        _globalHook = App.Current.Services.GetRequiredService<IGlobalHook>();
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

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        _hotkeyRegistry = App.Current.Services.GetService<INativeHotkeyRegistry>() as SharpHookHotkeyRegistry;
        if (_hotkeyRegistry != null)
        {
            _hotkeyRegistry.CheckGlobalHook();
            _hotkeyRegistry.SupressHotkey = true;
        }
        _globalHook.KeyReleased -= OnKeyUp;
        _globalHook.KeyReleased += OnKeyUp;
        _globalHook.KeyPressed -= OnKeyDown;
        _globalHook.KeyPressed += OnKeyDown;
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        if (_hotkeyRegistry != null)
        {
            _hotkeyRegistry.SupressHotkey = false;
        }
        _globalHook.KeyReleased -= OnKeyUp;
        _globalHook.KeyPressed -= OnKeyDown;
        base.OnLostFocus(e);
    }

    private void OnKeyUp(object? sender, KeyboardHookEventArgs e)
    {
        _pressingKeys.Remove(e.Data.KeyCode);
        e.SuppressEvent = true;
    }

    private void OnKeyDown(object? sender, KeyboardHookEventArgs e)
    {
        if (_pressingKeys.Count == 0)
        {
            _pressedKeys.Clear();
        }

        if (KeyCodeMap.Map.ContainsKey(e.Data.KeyCode))
        {
            _pressedKeys.Add(e.Data.KeyCode);
            _pressingKeys.Add(e.Data.KeyCode);
            UpdateHotkeyViewer();
        }
        e.SuppressEvent = true;
    }

    private void UpdateHotkeyViewer()
    {
        var keys = new List<Key>();
        foreach (var virtualKey in _pressedKeys)
        {
            if (KeyCodeMap.Map.TryGetValue(virtualKey, out Key key))
            {
                keys.Add(key);
            }
        }
        Dispatcher.UIThread.Post(() => Hotkey = new Hotkey(keys));
    }
}
