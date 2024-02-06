using SyncClipboard.Core.Models.Keyboard;
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
        [Key.Win] = HotKeyModifiers.MOD_WIN
    };

    public static readonly Dictionary<Key, VK> VirtualKeyMap = new()
    {
        [Key.A] = VK.VK_A,
        [Key.B] = VK.VK_B,
        [Key.C] = VK.VK_C,
        [Key.D] = VK.VK_D
    };
}
