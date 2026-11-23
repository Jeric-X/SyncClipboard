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
    private int? pendingPasteKey;
    private bool pastingRecord;
    private CancellationTokenSource? pastePreparation;

    public bool IsTrackingQuickPaste => hintConfig is not null || pendingPasteRecord.HasValue;

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

    public void CancelQuickPaste()
    {
        pastePreparation?.Cancel();
        ClearQuickPasteHints();
        pendingPasteKey = null;
        pendingPasteRecord = null;
    }

    private bool TryStartQuickPaste(HistoryShortcutConfig config, int keyIdentity, Hotkey hotkey)
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
        pendingPasteKey = keyIdentity;
        return true;
    }

    public async Task HandleKeyReleaseAsync(Key? key, Hotkey modifiers, int physicalKey = 0)
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

        if ((physicalKey == 0 ? ~(int?)key : physicalKey) == pendingPasteKey)
            pendingPasteKey = null;
        else if (pendingPasteKey.HasValue && (physicalKey == 0 || pendingPasteKey < 0) &&
            key is not (Key.Ctrl or Key.Shift or Key.Alt or Key.Meta))
        {
            // With no scan code, a layout change can make the release impossible to match reliably.
            CancelQuickPaste();
            return;
        }
        if (pendingPasteKey.HasValue || modifiers != Hotkey.Nothing || pendingPasteRecord is not { } target)
            return;
        pendingPasteRecord = null;
        pastingRecord = true;
        using var preparation = new CancellationTokenSource();
        pastePreparation = preparation;

        try
        {
            await RunWithOperationTimeoutAsync("quick paste", async token =>
            {
                var record = await historyManager.GetHistoryRecord(target.Hash, target.Type, token);
                if (record is not { IsDeleted: false } || IsMultiSelecting ||
                    !await SetRecordClipboardAsync(new HistoryRecordVM(record), closeAfterCopy: false, token))
                    return;
                token.ThrowIfCancellationRequested();
                // Changing focus is intentional from here onward.
                pastePreparation = null;
                await PasteToLastExternalWindowAsync(token);
            }, preparation.Token);
        }
        catch (Exception ex)
        {
            await logger.WriteAsync("Failed to paste history record:", ex.Message);
        }
        finally
        {
            pastePreparation = null;
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

    public HistoryRecordShortcutEditor CreateRecordShortcutEditor(HistoryRecordKey record) => new(_configManager, historyManager, record);
}
