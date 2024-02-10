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

    public static readonly Dictionary<VK, Key> VirtualKeyMap = new()
    {
        [VK.VK_CONTROL] = Key.Ctrl,
        [VK.VK_LCONTROL] = Key.Ctrl,
        [VK.VK_RCONTROL] = Key.Ctrl,
        [VK.VK_SHIFT] = Key.Shift,
        [VK.VK_LSHIFT] = Key.Shift,
        [VK.VK_RSHIFT] = Key.Shift,
        [VK.VK_LWIN] = Key.Win,
        [VK.VK_RWIN] = Key.Win,
        [VK.VK_MENU] = Key.Alt,
        [VK.VK_LMENU] = Key.Alt,
        [VK.VK_RMENU] = Key.Alt,
        [VK.VK_A] = Key.A,
        [VK.VK_B] = Key.B,
        [VK.VK_C] = Key.C,
        [VK.VK_D] = Key.D,
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
}
