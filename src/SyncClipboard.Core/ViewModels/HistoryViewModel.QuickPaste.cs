using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels.Sub;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel
{
    private HistoryShortcutConfig? hintConfig;
    private IReadOnlyList<HistoryRecordVM>? visibleQuickPasteRecords;
    private HistoryRecordKey[]? numberedQuickPasteRecords;
    private HistoryRecordKey? pendingPasteRecord;
    private Key? pendingPasteKey;
    private bool pastingRecord;

    private void UpdateQuickPastePrefix(HistoryShortcutConfig config, Hotkey modifiers)
    {
        var prefix = config.GetShortcut(HistoryShortcutAction.QuickPastePrefix);
        if (IsMultiSelecting || prefix == Hotkey.Nothing || !prefix.Keys.All(modifiers.Keys.Contains))
        {
            ClearQuickPasteHints();
            return;
        }
        if (hintConfig is null && modifiers != prefix)
            return;
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
        numberedQuickPasteRecords ??= visible.Take(9).Select(row => row.Key).ToArray();
        if (visibleQuickPasteRecords is not null && visible.SequenceEqual(visibleQuickPasteRecords, ReferenceEqualityComparer.Instance))
            return;

        ClearVisibleHints();
        visibleQuickPasteRecords = visible;
        foreach (var row in visible)
        {
            var binding = hintConfig.RecordShortcuts.FirstOrDefault(binding => binding.Record == row.Key);
            if (binding is not null)
            {
                row.QuickPasteKeys = HistoryKeycap.FromHotkey(binding.Hotkey);
                continue;
            }

            var index = Array.IndexOf(numberedQuickPasteRecords, row.Key);
            if (index >= 0)
                row.QuickPasteKeys = [new HistoryKeycap(Key._1 + index)];
        }
    }

    private void ClearVisibleHints()
    {
        if (visibleQuickPasteRecords is null)
            return;
        foreach (var record in visibleQuickPasteRecords)
            record.QuickPasteKeys = [];
        visibleQuickPasteRecords = null;
    }

    private void ClearQuickPasteHints()
    {
        hintConfig = null;
        numberedQuickPasteRecords = null;
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
        if (number > 0 && numberedQuickPasteRecords is { } records && number <= records.Length)
            target = records[number - 1];
        if (!target.HasValue)
            return false;
        if (pastingRecord)
            return true;

        // Wait for the physical modifiers to be released before sending Ctrl/Cmd+V.
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
