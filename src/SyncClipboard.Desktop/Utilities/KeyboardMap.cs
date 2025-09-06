using Avalonia.Input;
using System.Collections.Generic;
using AvaloniaKey = Avalonia.Input.Key;
using CoreKey = SyncClipboard.Core.Models.Keyboard.Key;

namespace SyncClipboard.Desktop.Utilities;

internal static class KeyboardMap
{
    public static readonly Dictionary<CoreKey, KeyModifiers> ModifierMap = new()
    {
        [CoreKey.Ctrl] = KeyModifiers.Control,
        [CoreKey.Shift] = KeyModifiers.Shift,
        [CoreKey.Alt] = KeyModifiers.Alt,
        [CoreKey.Meta] = KeyModifiers.Meta
    };

    public static readonly Dictionary<AvaloniaKey, CoreKey> AvaloniaKeyMap = new()
    {
        [AvaloniaKey.Back] = CoreKey.Backspace,
        [AvaloniaKey.Tab] = CoreKey.Tab,
        [AvaloniaKey.Clear] = CoreKey.Clear,
        // [AvaloniaKey.Return] = CoreKey.Enter,
        [AvaloniaKey.Enter] = CoreKey.Enter,
        [AvaloniaKey.LeftShift] = CoreKey.Shift,
        [AvaloniaKey.RightShift] = CoreKey.Shift,
        [AvaloniaKey.LeftCtrl] = CoreKey.Ctrl,
        [AvaloniaKey.RightCtrl] = CoreKey.Ctrl,
        [AvaloniaKey.LeftAlt] = CoreKey.Alt,
        [AvaloniaKey.RightAlt] = CoreKey.Alt,
        [AvaloniaKey.Pause] = CoreKey.Pause,
        [AvaloniaKey.Cancel] = CoreKey.Cancel,
        [AvaloniaKey.CapsLock] = CoreKey.Capital,
        [AvaloniaKey.Escape] = CoreKey.Esc,
        [AvaloniaKey.Space] = CoreKey.Space,
        [AvaloniaKey.PageUp] = CoreKey.PgUp,
        [AvaloniaKey.PageDown] = CoreKey.PgDn,
        [AvaloniaKey.End] = CoreKey.End,
        [AvaloniaKey.Home] = CoreKey.Home,
        [AvaloniaKey.Left] = CoreKey.Left,
        [AvaloniaKey.Up] = CoreKey.Up,
        [AvaloniaKey.Right] = CoreKey.Right,
        [AvaloniaKey.Down] = CoreKey.Down,
        [AvaloniaKey.Select] = CoreKey.Select,
        [AvaloniaKey.Print] = CoreKey.Print,
        [AvaloniaKey.Execute] = CoreKey.Execute,
        [AvaloniaKey.PrintScreen] = CoreKey.PrintScreen,
        [AvaloniaKey.Insert] = CoreKey.Insert,
        [AvaloniaKey.Delete] = CoreKey.Delete,
        [AvaloniaKey.Help] = CoreKey.Help,
        [AvaloniaKey.D0] = CoreKey._0,
        [AvaloniaKey.D1] = CoreKey._1,
        [AvaloniaKey.D2] = CoreKey._2,
        [AvaloniaKey.D3] = CoreKey._3,
        [AvaloniaKey.D4] = CoreKey._4,
        [AvaloniaKey.D5] = CoreKey._5,
        [AvaloniaKey.D6] = CoreKey._6,
        [AvaloniaKey.D7] = CoreKey._7,
        [AvaloniaKey.D8] = CoreKey._8,
        [AvaloniaKey.D9] = CoreKey._9,
        [AvaloniaKey.A] = CoreKey.A,
        [AvaloniaKey.B] = CoreKey.B,
        [AvaloniaKey.C] = CoreKey.C,
        [AvaloniaKey.D] = CoreKey.D,
        [AvaloniaKey.E] = CoreKey.E,
        [AvaloniaKey.F] = CoreKey.F,
        [AvaloniaKey.G] = CoreKey.G,
        [AvaloniaKey.H] = CoreKey.H,
        [AvaloniaKey.I] = CoreKey.I,
        [AvaloniaKey.J] = CoreKey.J,
        [AvaloniaKey.K] = CoreKey.K,
        [AvaloniaKey.L] = CoreKey.L,
        [AvaloniaKey.M] = CoreKey.M,
        [AvaloniaKey.N] = CoreKey.N,
        [AvaloniaKey.O] = CoreKey.O,
        [AvaloniaKey.P] = CoreKey.P,
        [AvaloniaKey.Q] = CoreKey.Q,
        [AvaloniaKey.R] = CoreKey.R,
        [AvaloniaKey.S] = CoreKey.S,
        [AvaloniaKey.T] = CoreKey.T,
        [AvaloniaKey.U] = CoreKey.U,
        [AvaloniaKey.V] = CoreKey.V,
        [AvaloniaKey.W] = CoreKey.W,
        [AvaloniaKey.X] = CoreKey.X,
        [AvaloniaKey.Y] = CoreKey.Y,
        [AvaloniaKey.Z] = CoreKey.Z,
        [AvaloniaKey.LWin] = CoreKey.Meta,
        [AvaloniaKey.RWin] = CoreKey.Meta,
        [AvaloniaKey.Apps] = CoreKey.Apps,
        [AvaloniaKey.Sleep] = CoreKey.Sleep,
        [AvaloniaKey.NumPad0] = CoreKey.NumPad0,
        [AvaloniaKey.NumPad1] = CoreKey.NumPad1,
        [AvaloniaKey.NumPad2] = CoreKey.NumPad2,
        [AvaloniaKey.NumPad3] = CoreKey.NumPad3,
        [AvaloniaKey.NumPad4] = CoreKey.NumPad4,
        [AvaloniaKey.NumPad5] = CoreKey.NumPad5,
        [AvaloniaKey.NumPad6] = CoreKey.NumPad6,
        [AvaloniaKey.NumPad7] = CoreKey.NumPad7,
        [AvaloniaKey.NumPad8] = CoreKey.NumPad8,
        [AvaloniaKey.NumPad9] = CoreKey.NumPad9,
        [AvaloniaKey.Multiply] = CoreKey.Multiply,
        [AvaloniaKey.Add] = CoreKey.Add,
        [AvaloniaKey.Separator] = CoreKey.Separator,
        [AvaloniaKey.Subtract] = CoreKey.Subtract,
        [AvaloniaKey.Decimal] = CoreKey.Decimal,
        [AvaloniaKey.Divide] = CoreKey.Divide,
        [AvaloniaKey.F1] = CoreKey.F1,
        [AvaloniaKey.F2] = CoreKey.F2,
        [AvaloniaKey.F3] = CoreKey.F3,
        [AvaloniaKey.F4] = CoreKey.F4,
        [AvaloniaKey.F5] = CoreKey.F5,
        [AvaloniaKey.F6] = CoreKey.F6,
        [AvaloniaKey.F7] = CoreKey.F7,
        [AvaloniaKey.F8] = CoreKey.F8,
        [AvaloniaKey.F9] = CoreKey.F9,
        [AvaloniaKey.F10] = CoreKey.F10,
        [AvaloniaKey.F11] = CoreKey.F11,
        [AvaloniaKey.F12] = CoreKey.F12,
        [AvaloniaKey.F13] = CoreKey.F13,
        [AvaloniaKey.F14] = CoreKey.F14,
        [AvaloniaKey.F15] = CoreKey.F15,
        [AvaloniaKey.F16] = CoreKey.F16,
        [AvaloniaKey.F17] = CoreKey.F17,
        [AvaloniaKey.F18] = CoreKey.F18,
        [AvaloniaKey.F19] = CoreKey.F19,
        [AvaloniaKey.F20] = CoreKey.F20,
        [AvaloniaKey.F21] = CoreKey.F21,
        [AvaloniaKey.F22] = CoreKey.F22,
        [AvaloniaKey.F23] = CoreKey.F23,
        [AvaloniaKey.F24] = CoreKey.F24,
        [AvaloniaKey.NumLock] = CoreKey.NumLock,
        [AvaloniaKey.Scroll] = CoreKey.Scroll,
        [AvaloniaKey.OemSemicolon] = CoreKey.Semicolon,
        [AvaloniaKey.OemPlus] = CoreKey.Equal,
        [AvaloniaKey.OemComma] = CoreKey.Comma,
        [AvaloniaKey.OemMinus] = CoreKey.Minus,
        [AvaloniaKey.OemPeriod] = CoreKey.Period,
        [AvaloniaKey.OemQuestion] = CoreKey.Slash,
        [AvaloniaKey.OemTilde] = CoreKey.BackQuote,
        [AvaloniaKey.OemOpenBrackets] = CoreKey.OpenBracket,
        [AvaloniaKey.OemPipe] = CoreKey.BackSlash,
        [AvaloniaKey.OemCloseBrackets] = CoreKey.CloshBracket,
        [AvaloniaKey.OemQuotes] = CoreKey.Quote,
        [AvaloniaKey.Oem8] = CoreKey.OEM_8,
        [AvaloniaKey.OemBackslash] = CoreKey.OEM_102,
        [AvaloniaKey.ImeProcessed] = CoreKey.PROCESSKEY,
        [AvaloniaKey.System] = CoreKey.ATTN,
        [AvaloniaKey.DbeAlphanumeric] = CoreKey.CRSEL,
        [AvaloniaKey.DbeKatakana] = CoreKey.EXSEL,
        [AvaloniaKey.DbeHiragana] = CoreKey.EREOF,
        [AvaloniaKey.DbeCodeInput] = CoreKey.PLAY,
        [AvaloniaKey.DbeNoCodeInput] = CoreKey.ZOOM,
        [AvaloniaKey.DbeDetermineString] = CoreKey.NONAME,
        [AvaloniaKey.DbeEnterDialogConversionMode] = CoreKey.PA1,
        [AvaloniaKey.DbeFlushString] = CoreKey.OEM_CLEAR,
    };

    /// <summary>
    /// 将Avalonia键值转换为Core键值
    /// </summary>
    /// <param name="avaloniaKey">Avalonia键值</param>
    /// <returns>Core键值，如果找不到映射则返回null</returns>
    public static CoreKey? ConvertFromAvalonia(AvaloniaKey avaloniaKey)
    {
        return AvaloniaKeyMap.TryGetValue(avaloniaKey, out var coreKey) ? coreKey : null;
    }

    /// <summary>
    /// 将Core键值转换为Avalonia键值
    /// </summary>
    /// <param name="coreKey">Core键值</param>
    /// <returns>Avalonia键值，如果找不到映射则返回null</returns>
    public static AvaloniaKey? ConvertToAvalonia(CoreKey coreKey)
    {
        foreach (var kvp in AvaloniaKeyMap)
        {
            if (kvp.Value == coreKey)
                return kvp.Key;
        }
        return null;
    }

    /// <summary>
    /// 将Core修饰键转换为Avalonia修饰键
    /// </summary>
    /// <param name="coreKey">Core修饰键</param>
    /// <returns>Avalonia修饰键，如果找不到映射则返回None</returns>
    public static KeyModifiers ConvertModifierToAvalonia(CoreKey coreKey)
    {
        return ModifierMap.TryGetValue(coreKey, out var modifier) ? modifier : KeyModifiers.None;
    }
}
