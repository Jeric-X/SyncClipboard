using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels.Sub;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel
{
    private HistoryShortcutConfig? hintConfig;
    private IReadOnlyList<HistoryRecordVM>? visibleQuickPasteRecords;
    private HistoryRecordKey? pendingPasteRecord;
    private Key? pendingPasteKey;
    private bool pastingRecord;

    private void UpdateQuickPastePrefix(HistoryShortcutConfig config, Hotkey modifiers)
    {
        if (IsMultiSelecting || modifiers == Hotkey.Nothing || modifiers != config.GetShortcut(HistoryShortcutAction.QuickPastePrefix))
        {
            ClearQuickPasteHints();
            return;
        }
        if (hintConfig is not null && !hintConfig.Equals(config))
            ClearVisibleHints();
        hintConfig = config;
        RefreshQuickPasteHints();
    }

    public void RefreshQuickPasteHints()
    {
        if (hintConfig is null)
            return;
        if (IsMultiSelecting || HasModalOperation)
        {
            CancelQuickPaste();
            return;
        }

        var visible = window.GetVisibleHistoryRecords();
        if (visibleQuickPasteRecords is not null && visible.SequenceEqual(visibleQuickPasteRecords, ReferenceEqualityComparer.Instance))
            return;

        ClearVisibleHints();
        visibleQuickPasteRecords = visible;
        for (var i = 0; i < visible.Count; i++)
        {
            var row = visible[i];
            var binding = hintConfig.RecordShortcuts.FirstOrDefault(binding => binding.Record == row.Key);
            if (binding is not null)
                row.QuickPasteHint = HistoryShortcutDescriptions.Format(binding.Hotkey);
            else if (i < 9)
                row.QuickPasteHint = (i + 1).ToString();
        }
    }

    private void ClearVisibleHints()
    {
        if (visibleQuickPasteRecords is null)
            return;
        foreach (var record in visibleQuickPasteRecords)
            record.QuickPasteHint = null;
        visibleQuickPasteRecords = null;
    }

    private void ClearQuickPasteHints()
    {
        hintConfig = null;
        ClearVisibleHints();
    }

    private void CancelQuickPaste()
    {
        ClearQuickPasteHints();
        pendingPasteKey = null;
        pendingPasteRecord = null;
    }

    private bool TryStartQuickPaste(HistoryShortcutConfig config, Key key, Hotkey hotkey)
    {
        if (pendingPasteRecord.HasValue)
            return true;

        var target = config.RecordShortcuts.FirstOrDefault(binding => binding.Hotkey == hotkey)?.Record;
        var number = HistoryShortcutConfig.GetQuickPasteNumber(config.GetShortcut(HistoryShortcutAction.QuickPastePrefix), hotkey);
        if (number > 0 && visibleQuickPasteRecords is { } rows && number <= rows.Count)
            target = rows[number - 1].Key;
        if (!target.HasValue)
            return false;
        if (pastingRecord)
            return true;

        // Capture the displayed target now. Release the physical modifiers before sending Ctrl/Cmd+V.
        pendingPasteRecord = target;
        pendingPasteKey = hotkey.Keys.Length == 1 ? key : null;
        return true;
    }

    public async Task HandleKeyReleaseAsync(Key key, Hotkey modifiers)
    {
        if (HasModalOperation || IsMultiSelecting)
        {
            CancelQuickPaste();
            return;
        }
        if (modifiers == Hotkey.Nothing)
            ClearQuickPasteHints();
        else
            UpdateQuickPastePrefix(_configManager.GetConfig<HistoryShortcutConfig>(), modifiers);

        if (key == pendingPasteKey)
            pendingPasteKey = null;
        if (pendingPasteKey.HasValue || modifiers != Hotkey.Nothing || pendingPasteRecord is not { } target)
            return;
        pendingPasteRecord = null;
        pastingRecord = true;

        try
        {
            await RunWithOperationTimeoutAsync("quick paste", async token =>
            {
                var record = await historyManager.GetHistoryRecord(target.Hash, target.Type, token);
                if (record is { IsDeleted: false })
                    await CopyToClipboard(new HistoryRecordVM(record), true, token);
            });
        }
        catch (Exception ex)
        {
            await logger.WriteAsync("Failed to paste history record:", ex.Message);
        }
        finally
        {
            pastingRecord = false;
        }
    }

    private void RemoveRecordShortcut(HistoryRecord record)
    {
        var key = HistoryRecordKey.From(record);
        _ = _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            var config = _configManager.GetConfig<HistoryShortcutConfig>();
            if (config.RecordShortcuts.Any(binding => binding.Record == key))
                _configManager.SetConfig(config.WithRecordShortcut(key, Hotkey.Nothing));
        });
    }

    public HistoryRecordShortcutEditor CreateRecordShortcutEditor(HistoryRecordKey record) => new(_configManager, record);
}
