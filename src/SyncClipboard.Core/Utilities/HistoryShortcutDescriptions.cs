using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities;

public static class HistoryShortcutDescriptions
{
    public static string GetActionName(HistoryShortcutAction action) => action switch
    {
        HistoryShortcutAction.Search => Strings.HistoryShortcutSearch,
        HistoryShortcutAction.PreviousItem => Strings.HistoryShortcutPreviousItem,
        HistoryShortcutAction.NextItem => Strings.HistoryShortcutNextItem,
        HistoryShortcutAction.FirstItem => Strings.HistoryShortcutFirstItem,
        HistoryShortcutAction.LastItem => Strings.HistoryShortcutLastItem,
        HistoryShortcutAction.ToggleStar => Strings.HistoryShortcutToggleStar,
        HistoryShortcutAction.ToggleStarredFilter => Strings.HistoryShortcutToggleStarredFilter,
        HistoryShortcutAction.ToggleTopmost => Strings.HistoryShortcutToggleTopmost,
        HistoryShortcutAction.Delete => Strings.HistoryShortcutDelete,
        HistoryShortcutAction.TogglePreview => Strings.HistoryShortcutTogglePreview,
        HistoryShortcutAction.NextFilter => Strings.HistoryShortcutNextFilter,
        HistoryShortcutAction.PreviousFilter => Strings.HistoryShortcutPreviousFilter,
        HistoryShortcutAction.CopyAndPaste => Strings.HistoryShortcutCopyAndPaste,
        HistoryShortcutAction.Copy => Strings.HistoryShortcutCopy,
        HistoryShortcutAction.ExecuteRecommendedAction => Strings.ExecuteRecommendedAction,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static string GetMouseActionName(HistoryMouseAction action) => action switch
    {
        HistoryMouseAction.Copy => Strings.HistoryShortcutCopy,
        HistoryMouseAction.CopyAndPaste => Strings.HistoryShortcutCopyAndPaste,
        HistoryMouseAction.ExecuteRecommendedAction => Strings.ExecuteRecommendedAction,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static string BuildInstructions(HistoryShortcutConfig config)
    {
        var keyboardLines = new List<string>();
        foreach (var action in Enum.GetValues<HistoryShortcutAction>())
        {
            var hotkey = config.GetShortcut(action);
            if (config.Match(hotkey) != action)
                continue;
            var keys = string.Join("+", hotkey.Keys.Select(key => key.ToEnumMemberValue()));
            keyboardLines.Add(string.Format(Strings.HistoryWindowOperationFormat, keys, GetActionName(action)));
        }

        var closeKeys = OperatingSystem.IsMacOS() ? "Esc / Ctrl+W / Cmd+W" : "Esc / Ctrl+W";
        keyboardLines.Add(string.Format(Strings.HistoryWindowOperationFormat, closeKeys, Strings.HistoryWindowCloseShortcutDescription));
        var mouseInstructions = string.Format(Strings.HistoryWindowMouseList,
            GetMouseActionName(config.DoubleClickAction), GetMouseActionName(config.MiddleClickAction));
        return $"{Strings.HistoryWindowKeyboardShortcuts}\n{string.Join('\n', keyboardLines)}\n\n" +
            $"{Strings.HistoryWindowMouseOperations}\n{mouseInstructions}";
    }
}
