using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Desktop.MacOS.Utilities;

[SupportedOSPlatform("macos")]
internal class CarbonHotkeyRegistry : INativeHotkeyRegistry, IDisposable
{
    private readonly Dictionary<Hotkey, uint> _registeredHotkeys = [];
    private readonly Dictionary<uint, Action> _hotkeyActions = [];
    private readonly Dictionary<uint, EventHotKeyRef> _hotkeyRefs = [];
    private uint _nextHotkeyId = 1;
    private IntPtr _eventHandler = IntPtr.Zero;
    private readonly EventHandlerUPP _eventHandlerUPP;

    // Carbon API constants
    private const uint kEventClassKeyboard = 1801812322; // 'keyb'
    private const uint kEventHotKeyPressed = 5;
    private const uint kEventHotKeyReleased = 6;
    private const uint kEventParamDirectObject = 0x2D2D2D2D; // '----' 
    private const uint typeEventHotKeyID = 0x686B6964; // 'hkid'

    // Modifier flags
    private const uint cmdKey = 0x0100;
    private const uint shiftKey = 0x0200;
    private const uint alphaLock = 0x0400;
    private const uint optionKey = 0x0800;
    private const uint controlKey = 0x1000;

    public CarbonHotkeyRegistry()
    {
        _eventHandlerUPP = new EventHandlerUPP(EventHandler);
        InstallApplicationEventHandler();
    }

    private void InstallApplicationEventHandler()
    {
        var eventTypes = new EventTypeSpec[]
        {
            new EventTypeSpec { eventClass = kEventClassKeyboard, eventKind = kEventHotKeyPressed },
            new EventTypeSpec { eventClass = kEventClassKeyboard, eventKind = kEventHotKeyReleased }
        };

        var result = InstallEventHandler(
            GetApplicationEventTarget(),
            _eventHandlerUPP,
            (uint)eventTypes.Length,
            eventTypes,
            IntPtr.Zero,
            out _eventHandler);

        if (result != 0)
        {
            throw new InvalidOperationException($"Failed to install event handler: {result}");
        }
    }

    private OSStatus EventHandler(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        var eventClass = GetEventClass(theEvent);
        var eventKind = GetEventKind(theEvent);

        if (eventClass == kEventClassKeyboard && eventKind == kEventHotKeyPressed)
        {
            var result = GetEventParameter(
                theEvent,
                kEventParamDirectObject,     // '----' - direct object parameter
                typeEventHotKeyID,           // 'hkid' - expected type
                IntPtr.Zero,                 // actual type (not needed)
                (uint)Marshal.SizeOf<EventHotKeyID>(), // buffer size
                IntPtr.Zero,                 // actual size (not needed)
                out EventHotKeyID hotKeyID);

            if (result == 0)
            {
                if (_hotkeyActions.TryGetValue(hotKeyID.id, out var action))
                {
                    try
                    {
                        action?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        // Log exception if needed
                        Console.WriteLine($"Error executing hotkey action: {ex.Message}");
                    }
                    return 0; // noErr
                }
            }
        }

        return CallNextEventHandler(nextHandler, theEvent);
    }

    public bool RegisterForSystemHotkey(Hotkey hotkey, Action action)
    {
        if (hotkey == null || action == null)
            return false;

        if (_registeredHotkeys.ContainsKey(hotkey))
            return false;

        var hotkeyId = _nextHotkeyId++;
        var (keyCode, modifiers) = ConvertHotkeyToCarbonFormat(hotkey);

        if (keyCode == uint.MaxValue)
            return false;

        var eventHotKeyId = new EventHotKeyID
        {
            signature = 1212498244, // 'HKEY'
            id = hotkeyId
        };

        var result = RegisterEventHotKey(
            keyCode,
            modifiers,
            eventHotKeyId,
            GetApplicationEventTarget(),
            0,
            out var hotkeyRef);

        if (result == 0)
        {
            _registeredHotkeys[hotkey] = hotkeyId;
            _hotkeyActions[hotkeyId] = action;
            _hotkeyRefs[hotkeyId] = hotkeyRef;
            return true;
        }

        return false;
    }

    public void UnRegisterForSystemHotkey(Hotkey hotkey)
    {
        if (!_registeredHotkeys.TryGetValue(hotkey, out var hotkeyId))
            return;

        if (_hotkeyRefs.TryGetValue(hotkeyId, out var hotkeyRef))
        {
            UnregisterEventHotKey(hotkeyRef);
            _hotkeyRefs.Remove(hotkeyId);
        }

        _registeredHotkeys.Remove(hotkey);
        _hotkeyActions.Remove(hotkeyId);
    }

    public bool IsValidHotkeyForm(Hotkey hotkey)
    {
        if (hotkey == null || hotkey.Keys.Length == 0)
            return false;

        if (hotkey.Keys.Length > 5)
            return false;

        var (keyCode, _) = ConvertHotkeyToCarbonFormat(hotkey);
        return keyCode != uint.MaxValue;
    }

    private static (uint keyCode, uint modifiers) ConvertHotkeyToCarbonFormat(Hotkey hotkey)
    {
        uint modifiers = 0;
        uint keyCode = uint.MaxValue;

        foreach (var key in hotkey.Keys)
        {
            switch (key)
            {
                case Key.Ctrl:
                    modifiers |= controlKey;
                    break;
                case Key.Shift:
                    modifiers |= shiftKey;
                    break;
                case Key.Alt:
                    modifiers |= optionKey;
                    break;
                case Key.Meta:
                    modifiers |= cmdKey;
                    break;
                case Key.Capital:
                    modifiers |= alphaLock;
                    break;
                default:
                    // Only one non-modifier key is allowed
                    if (keyCode != uint.MaxValue)
                        return (uint.MaxValue, 0);
                    keyCode = GetVirtualKeyCode(key);
                    break;
            }
        }

        return (keyCode, modifiers);
    }

    private static uint GetVirtualKeyCode(Key key)
    {
        // Map Core.Key to macOS virtual key codes
        return key switch
        {
            Key.A => 0x00,
            Key.B => 0x0B,
            Key.C => 0x08,
            Key.D => 0x02,
            Key.E => 0x0E,
            Key.F => 0x03,
            Key.G => 0x05,
            Key.H => 0x04,
            Key.I => 0x22,
            Key.J => 0x26,
            Key.K => 0x28,
            Key.L => 0x25,
            Key.M => 0x2E,
            Key.N => 0x2D,
            Key.O => 0x1F,
            Key.P => 0x23,
            Key.Q => 0x0C,
            Key.R => 0x0F,
            Key.S => 0x01,
            Key.T => 0x11,
            Key.U => 0x20,
            Key.V => 0x09,
            Key.W => 0x0D,
            Key.X => 0x07,
            Key.Y => 0x10,
            Key.Z => 0x06,

            Key._0 => 0x1D,
            Key._1 => 0x12,
            Key._2 => 0x13,
            Key._3 => 0x14,
            Key._4 => 0x15,
            Key._5 => 0x17,
            Key._6 => 0x16,
            Key._7 => 0x1A,
            Key._8 => 0x1C,
            Key._9 => 0x19,

            Key.F1 => 0x7A,
            Key.F2 => 0x78,
            Key.F3 => 0x63,
            Key.F4 => 0x76,
            Key.F5 => 0x60,
            Key.F6 => 0x61,
            Key.F7 => 0x62,
            Key.F8 => 0x64,
            Key.F9 => 0x65,
            Key.F10 => 0x6D,
            Key.F11 => 0x67,
            Key.F12 => 0x6F,

            Key.Space => 0x31,
            Key.Enter => 0x24,
            Key.Backspace => 0x33,
            Key.Tab => 0x30,
            Key.Esc => 0x35,
            Key.Delete => 0x75,
            Key.Home => 0x73,
            Key.End => 0x77,
            Key.PgUp => 0x74,
            Key.PgDn => 0x79,
            Key.Left => 0x7B,
            Key.Right => 0x7C,
            Key.Down => 0x7D,
            Key.Up => 0x7E,

            Key.Minus => 0x1B,
            Key.Equal => 0x18,
            Key.OpenBracket => 0x21,
            Key.CloshBracket => 0x1E,
            Key.BackSlash => 0x2A,
            Key.Semicolon => 0x29,
            Key.Quote => 0x27,
            Key.BackQuote => 0x32,
            Key.Comma => 0x2B,
            Key.Period => 0x2F,
            Key.Slash => 0x2C,

            _ => 0
        };
    }

    public void Dispose()
    {
        // Unregister all hotkeys
        foreach (var hotkeyRef in _hotkeyRefs.Values)
        {
            UnregisterEventHotKey(hotkeyRef);
        }

        // Remove event handler
        if (_eventHandler != IntPtr.Zero)
        {
            RemoveEventHandler(_eventHandler);
            _eventHandler = IntPtr.Zero;
        }

        _registeredHotkeys.Clear();
        _hotkeyActions.Clear();
        _hotkeyRefs.Clear();

        GC.SuppressFinalize(this);
    }

    ~CarbonHotkeyRegistry()
    {
        Dispose();
    }

    #region Carbon API Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint eventClass;
        public uint eventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyID
    {
        public uint signature;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyRef
    {
        public IntPtr value;
    }

    private delegate OSStatus EventHandlerUPP(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    private delegate OSStatus EventHandlerProcPtr(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);
    const string CarbonLib = "/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon";
    [DllImport(CarbonLib)]
    private static extern OSStatus InstallEventHandler(
        IntPtr eventTargetRef,
        EventHandlerUPP handler,
        uint numTypes,
        [In] EventTypeSpec[] typeList,
        IntPtr userData,
        out IntPtr handlerRef);

    [DllImport(CarbonLib)]
    private static extern OSStatus RemoveEventHandler(IntPtr handlerRef);

    [DllImport(CarbonLib)]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport(CarbonLib)]
    private static extern OSStatus RegisterEventHotKey(
        uint keyCode,
        uint modifiers,
        EventHotKeyID hotkeyId,
        IntPtr target,
        uint options,
        out EventHotKeyRef hotkeyRef);

    [DllImport(CarbonLib)]
    private static extern OSStatus UnregisterEventHotKey(EventHotKeyRef hotkeyRef);

    [DllImport(CarbonLib)]
    private static extern uint GetEventClass(IntPtr theEvent);

    [DllImport(CarbonLib)]
    private static extern uint GetEventKind(IntPtr theEvent);

    [DllImport(CarbonLib)]
    private static extern OSStatus GetEventParameter(
        IntPtr inEvent,
        uint inName,
        uint inDesiredType,
        IntPtr outActualType,  // can be IntPtr.Zero
        uint inBufferSize,
        IntPtr outActualSize,  // can be IntPtr.Zero
        out EventHotKeyID outData);

    [DllImport(CarbonLib)]
    private static extern OSStatus CallNextEventHandler(IntPtr nextHandler, IntPtr theEvent);

    private enum OSStatus : int
    {
        noErr = 0,
        eventParameterNotFoundErr = -9870,
        eventNotHandledErr = -9874,
        eventAlreadyPostedErr = -9860,
        eventTargetBusyErr = -9861,
        eventDeferAccessibilityEventErr = -9865
    }

    #endregion
}