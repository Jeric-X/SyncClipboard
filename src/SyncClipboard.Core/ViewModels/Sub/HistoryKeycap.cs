using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels.Sub;

public sealed class HistoryKeycap(Key key)
{
    public string Name { get; } = key == Key.Meta && OperatingSystem.IsLinux() ? "Super" : key.ToEnumMemberValue();
    public bool ShowWindowsLogo => key == Key.Meta && OperatingSystem.IsWindows();

    public string Label => OperatingSystem.IsMacOS() ? key switch
    {
        Key.Ctrl => "⌃",
        Key.Alt => "⌥",
        Key.Shift => "⇧",
        Key.Meta => "⌘",
        Key.Enter => "↩",
        Key.Backspace => "⌫",
        Key.Delete => "⌦",
        Key.Tab => "⇥",
        Key.Esc => "⎋",
        _ => Name
    } : Name;

    public static HistoryKeycap[] FromHotkey(Hotkey hotkey) =>
        hotkey.Keys.OrderBy(ModifierOrder).Select(key => new HistoryKeycap(key)).ToArray();

    private static int ModifierOrder(Key key) => key switch
    {
        Key.Meta => OperatingSystem.IsMacOS() ? 3 : -1,
        Key.Ctrl => 0,
        Key.Alt => 1,
        Key.Shift => 2,
        _ => 4
    };
}
