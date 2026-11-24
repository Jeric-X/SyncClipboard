using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities;

public static class HistoryShortcutDescriptions
{
    public static string GetActionName(HistoryShortcutAction action) => action switch
    {
        HistoryShortcutAction.QuickActionPrefix => Strings.QuickActionPrefix,
        HistoryShortcutAction.Search => Strings.HistoryShortcutSearch,
        HistoryShortcutAction.EditTags => Strings.HistoryTagsEdit,
        HistoryShortcutAction.EditTitle => Strings.HistoryEditTitle,
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
        HistoryShortcutAction.PreviousTag => Strings.HistoryShortcutPreviousTag,
        HistoryShortcutAction.NextTag => Strings.HistoryShortcutNextTag,
        HistoryShortcutAction.ToggleTagsExpanded => Strings.HistoryShortcutToggleTagsExpanded,
        HistoryShortcutAction.TagAbove => Strings.HistoryShortcutTagAbove,
        HistoryShortcutAction.TagBelow => Strings.HistoryShortcutTagBelow,
        HistoryShortcutAction.CopyAndPaste => Strings.HistoryShortcutCopyAndPaste,
        HistoryShortcutAction.Copy => Strings.HistoryShortcutCopy,
        HistoryShortcutAction.ExecuteRecommendedAction => Strings.ExecuteRecommendedAction,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static string Format(Hotkey hotkey) => string.Join("+", hotkey.Keys.Select(key => key.ToEnumMemberValue()));

    public static string GetErrorMessage(Hotkey hotkey, HistoryShortcutError error, HistoryShortcutAction? conflictingAction)
    {
        var keys = Format(hotkey);
        return error switch
        {
            HistoryShortcutError.PrefixModifiersOnly => Strings.QuickActionModifiersOnly,
            HistoryShortcutError.RecordConflict => string.Format(Strings.HistoryShortcutConflict, keys, Strings.HistoryRecordPasteBinding),
            HistoryShortcutError.None => string.Empty,
            HistoryShortcutError.Reserved => string.Format(Strings.HistoryShortcutReserved, keys),
            HistoryShortcutError.ModifierOnly => Strings.HistoryShortcutModifierOnly,
            HistoryShortcutError.MultipleMainKeys => Strings.HistoryShortcutMultipleMainKeys,
            HistoryShortcutError.SingleInputCharacter => string.Format(Strings.HistoryShortcutInputCharacter, keys),
            HistoryShortcutError.Conflict => string.Format(Strings.HistoryShortcutConflict,
                keys, GetActionName(conflictingAction!.Value)),
            _ => throw new ArgumentOutOfRangeException(nameof(error))
        };
    }

    public static string GetQuickActionName(HistoryQuickAction action) => action switch
    {
        HistoryQuickAction.Copy => Strings.HistoryShortcutCopy,
        HistoryQuickAction.Paste => Strings.HistoryQuickActionPaste,
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
            if (action == HistoryShortcutAction.QuickActionPrefix && hotkey != Hotkey.Nothing)
            {
                var prefix = Format(hotkey);
                var description = string.Format(Strings.QuickActionVisibleRecord, GetQuickActionName(config.QuickAction));
                keyboardLines.Add(string.Format(Strings.HistoryWindowOperationFormat, $"{prefix}+1–9", description));
            }
            if (config.Match(hotkey) != action)
                continue;
            var keys = Format(hotkey);
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
