﻿using SyncClipboard.Core.Models.Keyboard;
using System.Collections.Generic;
using static Vanara.PInvoke.User32;

namespace SyncClipboard.WinUI3.Win32;

internal static class KeyboardMap
{
    public static readonly Dictionary<Key, HotKeyModifiers> ModifierMap = new()
    {
        [Key.Ctrl] = HotKeyModifiers.MOD_CONTROL,
        [Key.Shift] = HotKeyModifiers.MOD_SHIFT,
        [Key.Alt] = HotKeyModifiers.MOD_ALT,
        [Key.Meta] = HotKeyModifiers.MOD_WIN
    };

    public static readonly Dictionary<VK, Key> VirtualKeyMap = new()
    {
        [VK.VK_BACK] = Key.Backspace,
        [VK.VK_TAB] = Key.Tab,
        [VK.VK_CLEAR] = Key.Clear,
        [VK.VK_RETURN] = Key.Enter,
        [VK.VK_SHIFT] = Key.Shift,
        [VK.VK_CONTROL] = Key.Ctrl,
        [VK.VK_MENU] = Key.Alt,
        [VK.VK_PAUSE] = Key.Pause,
        [VK.VK_CANCEL] = Key.Cancel,
        [VK.VK_CAPITAL] = Key.Capital,
        //[VK.VK_KANA] = Key.KANA,
        //[VK.VK_HANGUEL] = Key.HANGUEL,
        [VK.VK_HANGUL] = Key.Hangul,
        [VK.VK_IME_ON] = Key.ImeOn,
        [VK.VK_JUNJA] = Key.Junja,
        [VK.VK_FINAL] = Key.Final,
        //[VK.VK_HANJA] = Key.HANJA,
        [VK.VK_KANJI] = Key.Kanji,
        [VK.VK_IME_OFF] = Key.ImeOff,
        [VK.VK_ESCAPE] = Key.Esc,
        [VK.VK_CONVERT] = Key.Convert,
        [VK.VK_NONCONVERT] = Key.Nonconvert,
        [VK.VK_ACCEPT] = Key.Accept,
        [VK.VK_MODECHANGE] = Key.Modechange,
        [VK.VK_SPACE] = Key.Space,
        [VK.VK_PRIOR] = Key.PgUp,
        [VK.VK_NEXT] = Key.PgDn,
        [VK.VK_END] = Key.End,
        [VK.VK_HOME] = Key.Home,
        [VK.VK_LEFT] = Key.Left,
        [VK.VK_UP] = Key.Up,
        [VK.VK_RIGHT] = Key.Right,
        [VK.VK_DOWN] = Key.Down,
        [VK.VK_SELECT] = Key.Select,
        [VK.VK_PRINT] = Key.Print,
        [VK.VK_EXECUTE] = Key.Execute,
        [VK.VK_SNAPSHOT] = Key.PrintScreen,
        [VK.VK_INSERT] = Key.Insert,
        [VK.VK_DELETE] = Key.Delete,
        [VK.VK_HELP] = Key.Help,
        [VK.VK_0] = Key._0,
        [VK.VK_1] = Key._1,
        [VK.VK_2] = Key._2,
        [VK.VK_3] = Key._3,
        [VK.VK_4] = Key._4,
        [VK.VK_5] = Key._5,
        [VK.VK_6] = Key._6,
        [VK.VK_7] = Key._7,
        [VK.VK_8] = Key._8,
        [VK.VK_9] = Key._9,
        [VK.VK_A] = Key.A,
        [VK.VK_B] = Key.B,
        [VK.VK_C] = Key.C,
        [VK.VK_D] = Key.D,
        [VK.VK_E] = Key.E,
        [VK.VK_F] = Key.F,
        [VK.VK_G] = Key.G,
        [VK.VK_H] = Key.H,
        [VK.VK_I] = Key.I,
        [VK.VK_J] = Key.J,
        [VK.VK_K] = Key.K,
        [VK.VK_L] = Key.L,
        [VK.VK_M] = Key.M,
        [VK.VK_N] = Key.N,
        [VK.VK_O] = Key.O,
        [VK.VK_P] = Key.P,
        [VK.VK_Q] = Key.Q,
        [VK.VK_R] = Key.R,
        [VK.VK_S] = Key.S,
        [VK.VK_T] = Key.T,
        [VK.VK_U] = Key.U,
        [VK.VK_V] = Key.V,
        [VK.VK_W] = Key.W,
        [VK.VK_X] = Key.X,
        [VK.VK_Y] = Key.Y,
        [VK.VK_Z] = Key.Z,
        [VK.VK_LWIN] = Key.Meta,
        [VK.VK_RWIN] = Key.Meta,
        [VK.VK_APPS] = Key.Apps,
        [VK.VK_SLEEP] = Key.Sleep,
        [VK.VK_NUMPAD0] = Key.NumPad0,
        [VK.VK_NUMPAD1] = Key.NumPad1,
        [VK.VK_NUMPAD2] = Key.NumPad2,
        [VK.VK_NUMPAD3] = Key.NumPad3,
        [VK.VK_NUMPAD4] = Key.NumPad4,
        [VK.VK_NUMPAD5] = Key.NumPad5,
        [VK.VK_NUMPAD6] = Key.NumPad6,
        [VK.VK_NUMPAD7] = Key.NumPad7,
        [VK.VK_NUMPAD8] = Key.NumPad8,
        [VK.VK_NUMPAD9] = Key.NumPad9,
        [VK.VK_MULTIPLY] = Key.Multiply,
        [VK.VK_ADD] = Key.Add,
        [VK.VK_SEPARATOR] = Key.Separator,
        [VK.VK_SUBTRACT] = Key.Subtract,
        [VK.VK_DECIMAL] = Key.Decimal,
        [VK.VK_DIVIDE] = Key.Divide,
        [VK.VK_F1] = Key.F1,
        [VK.VK_F2] = Key.F2,
        [VK.VK_F3] = Key.F3,
        [VK.VK_F4] = Key.F4,
        [VK.VK_F5] = Key.F5,
        [VK.VK_F6] = Key.F6,
        [VK.VK_F7] = Key.F7,
        [VK.VK_F8] = Key.F8,
        [VK.VK_F9] = Key.F9,
        [VK.VK_F10] = Key.F10,
        [VK.VK_F11] = Key.F11,
        [VK.VK_F12] = Key.F12,
        [VK.VK_F13] = Key.F13,
        [VK.VK_F14] = Key.F14,
        [VK.VK_F15] = Key.F15,
        [VK.VK_F16] = Key.F16,
        [VK.VK_F17] = Key.F17,
        [VK.VK_F18] = Key.F18,
        [VK.VK_F19] = Key.F19,
        [VK.VK_F20] = Key.F20,
        [VK.VK_F21] = Key.F21,
        [VK.VK_F22] = Key.F22,
        [VK.VK_F23] = Key.F23,
        [VK.VK_F24] = Key.F24,
        [VK.VK_NUMLOCK] = Key.NumLock,
        [VK.VK_SCROLL] = Key.Scroll,
        [VK.VK_LSHIFT] = Key.Shift,
        [VK.VK_RSHIFT] = Key.Shift,
        [VK.VK_LCONTROL] = Key.Ctrl,
        [VK.VK_RCONTROL] = Key.Ctrl,
        [VK.VK_LMENU] = Key.Alt,
        [VK.VK_RMENU] = Key.Alt,
        [VK.VK_BROWSER_BACK] = Key.BrowserBack,
        [VK.VK_BROWSER_FORWARD] = Key.BrowserForward,
        [VK.VK_BROWSER_REFRESH] = Key.BrowserRefresh,
        [VK.VK_BROWSER_STOP] = Key.BrowserStop,
        [VK.VK_BROWSER_SEARCH] = Key.BrowserSearch,
        [VK.VK_BROWSER_FAVORITES] = Key.BrowserFavorites,
        [VK.VK_BROWSER_HOME] = Key.BrowserHome,
        [VK.VK_VOLUME_MUTE] = Key.VolumeMute,
        [VK.VK_VOLUME_DOWN] = Key.VolumeDown,
        [VK.VK_VOLUME_UP] = Key.VolumeUp,
        [VK.VK_MEDIA_NEXT_TRACK] = Key.MediaNextTrack,
        [VK.VK_MEDIA_PREV_TRACK] = Key.MediaPrevTrack,
        [VK.VK_MEDIA_STOP] = Key.MediaStop,
        [VK.VK_MEDIA_PLAY_PAUSE] = Key.MediaPlayPause,
        [VK.VK_LAUNCH_MAIL] = Key.LaunchMail,
        [VK.VK_LAUNCH_MEDIA_SELECT] = Key.LaunchMediaSelect,
        [VK.VK_LAUNCH_APP1] = Key.LaunchApp1,
        [VK.VK_LAUNCH_APP2] = Key.LaunchApp2,
        [VK.VK_OEM_1] = Key.Semicolon,
        [VK.VK_OEM_PLUS] = Key.Equal,
        [VK.VK_OEM_COMMA] = Key.Comma,
        [VK.VK_OEM_MINUS] = Key.Minus,
        [VK.VK_OEM_PERIOD] = Key.Period,
        [VK.VK_OEM_2] = Key.Slash,
        [VK.VK_OEM_3] = Key.BackQuote,
        [VK.VK_OEM_4] = Key.OpenBracket,
        [VK.VK_OEM_5] = Key.BackSlash,
        [VK.VK_OEM_6] = Key.CloshBracket,
        [VK.VK_OEM_7] = Key.Quote,
        [VK.VK_OEM_FJ_JISHO] = Key.OEM_FJ_JISHO,
        [VK.VK_OEM_FJ_MASSHOU] = Key.OEM_FJ_MASSHOU,
        [VK.VK_OEM_FJ_TOUROKU] = Key.OEM_FJ_TOUROKU,
        [VK.VK_OEM_FJ_LOYA] = Key.OEM_FJ_LOYA,
        [VK.VK_OEM_FJ_ROYA] = Key.OEM_FJ_ROYA,
        [VK.VK_GAMEPAD_A] = Key.GAMEPAD_A,
        [VK.VK_GAMEPAD_B] = Key.GAMEPAD_B,
        [VK.VK_GAMEPAD_X] = Key.GAMEPAD_X,
        [VK.VK_GAMEPAD_Y] = Key.GAMEPAD_Y,
        [VK.VK_GAMEPAD_RIGHT_SHOULDER] = Key.GAMEPAD_RIGHT_SHOULDER,
        [VK.VK_GAMEPAD_LEFT_SHOULDER] = Key.GAMEPAD_LEFT_SHOULDER,
        [VK.VK_GAMEPAD_LEFT_TRIGGER] = Key.GAMEPAD_LEFT_TRIGGER,
        [VK.VK_GAMEPAD_RIGHT_TRIGGER] = Key.GAMEPAD_RIGHT_TRIGGER,
        [VK.VK_GAMEPAD_DPAD_UP] = Key.GAMEPAD_DPAD_UP,
        [VK.VK_GAMEPAD_DPAD_DOWN] = Key.GAMEPAD_DPAD_DOWN,
        [VK.VK_GAMEPAD_DPAD_LEFT] = Key.GAMEPAD_DPAD_LEFT,
        [VK.VK_GAMEPAD_DPAD_RIGHT] = Key.GAMEPAD_DPAD_RIGHT,
        [VK.VK_GAMEPAD_MENU] = Key.GAMEPAD_MENU,
        [VK.VK_GAMEPAD_VIEW] = Key.GAMEPAD_VIEW,
        [VK.VK_GAMEPAD_LEFT_THUMBSTICK_BUTTON] = Key.GAMEPAD_LEFT_THUMBSTICK_BUTTON,
        [VK.VK_GAMEPAD_RIGHT_THUMBSTICK_BUTTON] = Key.GAMEPAD_RIGHT_THUMBSTICK_BUTTON,
        [VK.VK_GAMEPAD_LEFT_THUMBSTICK_UP] = Key.GAMEPAD_LEFT_THUMBSTICK_UP,
        [VK.VK_GAMEPAD_LEFT_THUMBSTICK_DOWN] = Key.GAMEPAD_LEFT_THUMBSTICK_DOWN,
        [VK.VK_GAMEPAD_LEFT_THUMBSTICK_RIGHT] = Key.GAMEPAD_LEFT_THUMBSTICK_RIGHT,
        [VK.VK_GAMEPAD_LEFT_THUMBSTICK_LEFT] = Key.GAMEPAD_LEFT_THUMBSTICK_LEFT,
        [VK.VK_GAMEPAD_RIGHT_THUMBSTICK_UP] = Key.GAMEPAD_RIGHT_THUMBSTICK_UP,
        [VK.VK_GAMEPAD_RIGHT_THUMBSTICK_DOWN] = Key.GAMEPAD_RIGHT_THUMBSTICK_DOWN,
        [VK.VK_GAMEPAD_RIGHT_THUMBSTICK_RIGHT] = Key.GAMEPAD_RIGHT_THUMBSTICK_RIGHT,
        [VK.VK_GAMEPAD_RIGHT_THUMBSTICK_LEFT] = Key.GAMEPAD_RIGHT_THUMBSTICK_LEFT,
        [VK.VK_OEM_8] = Key.OEM_8,
        [VK.VK_OEM_AX] = Key.OEM_AX,
        [VK.VK_OEM_102] = Key.OEM_102,
        [VK.VK_PROCESSKEY] = Key.PROCESSKEY,
        [VK.VK_ICO_CLEAR] = Key.ICO_CLEAR,
        [VK.VK_PACKET] = Key.PACKET,
        [VK.VK_OEM_RESET] = Key.OEM_RESET,
        [VK.VK_OEM_JUMP] = Key.OEM_JUMP,
        [VK.VK_OEM_PA1] = Key.OEM_PA1,
        [VK.VK_OEM_PA2] = Key.OEM_PA2,
        [VK.VK_OEM_PA3] = Key.OEM_PA3,
        [VK.VK_OEM_WSCTRL] = Key.OEM_WSCTRL,
        [VK.VK_OEM_CUSEL] = Key.OEM_CUSEL,
        [VK.VK_OEM_ATTN] = Key.OEM_ATTN,
        [VK.VK_OEM_FINISH] = Key.OEM_FINISH,
        [VK.VK_OEM_COPY] = Key.OEM_COPY,
        [VK.VK_OEM_AUTO] = Key.OEM_AUTO,
        [VK.VK_OEM_ENLW] = Key.OEM_ENLW,
        [VK.VK_OEM_BACKTAB] = Key.OEM_BACKTAB,
        [VK.VK_ATTN] = Key.ATTN,
        [VK.VK_CRSEL] = Key.CRSEL,
        [VK.VK_EXSEL] = Key.EXSEL,
        [VK.VK_EREOF] = Key.EREOF,
        [VK.VK_PLAY] = Key.PLAY,
        [VK.VK_ZOOM] = Key.ZOOM,
        [VK.VK_NONAME] = Key.NONAME,
        [VK.VK_PA1] = Key.PA1,
        [VK.VK_OEM_CLEAR] = Key.OEM_CLEAR,
    };

    public static readonly Dictionary<Key, VK> VirtualKeyMapReverse = Reverse(VirtualKeyMap);

    private static Dictionary<TV, TK> Reverse<TK, TV>(Dictionary<TK, TV> oldDict) where TV : notnull where TK : notnull
    {
        var newDict = new Dictionary<TV, TK>();
        foreach (var (key, value) in oldDict)
        {
            newDict.TryAdd(value, key);
        }

        return newDict;
    }

    /// <summary>
    /// 将WinUI3 VirtualKey转换为Core键值
    /// </summary>
    /// <param name="virtualKey">WinUI3 VirtualKey</param>
    /// <returns>Core键值，如果找不到映射则返回null</returns>
    public static Key? ConvertFromVirtualKey(Windows.System.VirtualKey virtualKey)
    {
        // 将WinUI3 VirtualKey转换为VK枚举
        var vk = (VK)(int)virtualKey;
        return VirtualKeyMap.TryGetValue(vk, out var coreKey) ? coreKey : null;
    }

    /// <summary>
    /// 将Core键值转换为WinUI3 VirtualKey
    /// </summary>
    /// <param name="coreKey">Core键值</param>
    /// <returns>WinUI3 VirtualKey，如果找不到映射则返回null</returns>
    public static Windows.System.VirtualKey? ConvertToVirtualKey(Key coreKey)
    {
        if (VirtualKeyMapReverse.TryGetValue(coreKey, out var vk))
        {
            return (Windows.System.VirtualKey)(int)vk;
        }
        return null;
    }
}
