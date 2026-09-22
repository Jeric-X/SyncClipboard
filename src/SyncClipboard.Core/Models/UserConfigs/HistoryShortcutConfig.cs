using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

public enum HistoryShortcutAction
{
    Search, PreviousItem, NextItem, FirstItem, LastItem, ToggleStar, ToggleStarredFilter,
    ToggleTopmost, Delete, TogglePreview, NextFilter, PreviousFilter, CopyAndPaste, Copy, ExecuteRecommendedAction
}

public enum HistoryMouseAction
{
    Copy = 1,
    CopyAndPaste = 2,
    ExecuteRecommendedAction = 6
}

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record HistoryShortcutConfig
{
    public const string ConfigKey = "HistoryShortcuts";

    public Dictionary<HistoryShortcutAction, Hotkey> Shortcuts { get; set; } = [];
    private HistoryMouseAction doubleClickAction = HistoryMouseAction.Copy;
    private HistoryMouseAction middleClickAction = HistoryMouseAction.CopyAndPaste;

    public HistoryMouseAction DoubleClickAction
    {
        get => doubleClickAction;
        set => doubleClickAction = Enum.IsDefined(value) ? value : HistoryMouseAction.Copy;
    }

    public HistoryMouseAction MiddleClickAction
    {
        get => middleClickAction;
        set => middleClickAction = Enum.IsDefined(value) ? value : HistoryMouseAction.CopyAndPaste;
    }

    public static Hotkey GetDefault(HistoryShortcutAction action) => action switch
    {
        HistoryShortcutAction.Search => new(Key.Ctrl, Key.F),
        HistoryShortcutAction.PreviousItem => new(Key.Up),
        HistoryShortcutAction.NextItem => new(Key.Down),
        HistoryShortcutAction.FirstItem => new(Key.Ctrl, Key.Home),
        HistoryShortcutAction.LastItem => new(Key.Ctrl, Key.End),
        HistoryShortcutAction.ToggleStar => new(Key.Ctrl, Key.S),
        HistoryShortcutAction.ToggleStarredFilter => new(Key.Ctrl, Key.Shift, Key.S),
        HistoryShortcutAction.ToggleTopmost => new(Key.Ctrl, Key.Shift, Key.T),
        HistoryShortcutAction.Delete => new(Key.Ctrl, Key.D),
        HistoryShortcutAction.TogglePreview => new(Key.Ctrl, Key.P),
        HistoryShortcutAction.NextFilter => new(Key.Tab),
        HistoryShortcutAction.PreviousFilter => new(Key.Shift, Key.Tab),
        HistoryShortcutAction.CopyAndPaste => new(Key.Enter),
        HistoryShortcutAction.Copy => new(Key.Alt, Key.Enter),
        _ => Hotkey.Nothing
    };

    public Hotkey GetShortcut(HistoryShortcutAction action) =>
        Shortcuts.TryGetValue(action, out var hotkey) ? hotkey : GetDefault(action);

    public static bool IsReserved(Hotkey hotkey) => hotkey.Keys.Contains(Key.Esc)
        || hotkey == new Hotkey(Key.Ctrl, Key.W)
        || (OperatingSystem.IsMacOS() && hotkey == new Hotkey(Key.Meta, Key.W));

    public bool CanAssign(HistoryShortcutAction action, Hotkey hotkey) =>
        hotkey == Hotkey.Nothing || (!IsReserved(hotkey)
            && hotkey.Keys.Count(key => key is not (Key.Ctrl or Key.Shift or Key.Alt or Key.Meta)) == 1
            && Enum.GetValues<HistoryShortcutAction>().All(other => other == action || GetShortcut(other) != hotkey));

    public HistoryShortcutAction? Match(Hotkey hotkey)
    {
        if (hotkey == Hotkey.Nothing || IsReserved(hotkey))
            return null;
        foreach (var action in Enum.GetValues<HistoryShortcutAction>())
        {
            if (GetShortcut(action) == hotkey)
                return action;
        }
        return null;
    }

    public virtual bool Equals(HistoryShortcutConfig? other) => other is not null
        && DoubleClickAction == other.DoubleClickAction
        && MiddleClickAction == other.MiddleClickAction
        && Shortcuts.Count == other.Shortcuts.Count
        && !Shortcuts.Except(other.Shortcuts).Any();

    public override int GetHashCode() => HashCode.Combine(
        DoubleClickAction, MiddleClickAction,
        Shortcuts.Aggregate(0, (hash, pair) => hash ^ pair.GetHashCode()));
}
