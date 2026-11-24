using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels.Sub;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel
{
    private HistoryShortcutConfig? hintConfig;
    private IReadOnlyList<HistoryRecordVM>? visibleQuickActionRecords;
    private HistoryRecordKey[]? numberedQuickActionRecords;
    internal Task? ActiveQuickAction { get; private set; }

    public bool ShowingQuickActionHints => hintConfig is not null;

    private void UpdateQuickActionPrefix(HistoryShortcutConfig config, Hotkey modifiers)
    {
        var prefix = config.GetShortcut(HistoryShortcutAction.QuickActionPrefix);
        if (IsMultiSelecting || prefix == Hotkey.Nothing || !prefix.Keys.All(modifiers.Keys.Contains))
        {
            ClearQuickActionHints();
            return;
        }
        if (hintConfig is null && modifiers != prefix)
            return;
        if (hintConfig is not null && !hintConfig.Equals(config))
            ClearVisibleHints();
        hintConfig = config;
        RefreshQuickActionHints();
    }

    public void RefreshQuickActionHints()
    {
        if (hintConfig is null)
            return;
        if (IsMultiSelecting || HasModalOperation)
        {
            ClearQuickActionHints();
            return;
        }

        var visible = window.GetVisibleHistoryRecords();
        numberedQuickActionRecords ??= visible.Take(9).Select(row => row.Key).ToArray();
        if (visibleQuickActionRecords is not null && visible.SequenceEqual(visibleQuickActionRecords, ReferenceEqualityComparer.Instance))
            return;

        ClearVisibleHints();
        visibleQuickActionRecords = visible;
        var prefix = hintConfig.GetShortcut(HistoryShortcutAction.QuickActionPrefix);
        foreach (var row in visible)
        {
            var binding = hintConfig.RecordShortcuts.FirstOrDefault(binding => binding.Record == row.Key);
            if (binding is not null && hintConfig.Match(binding.Hotkey) is null)
            {
                row.QuickActionKeys = HistoryKeycap.FromHotkey(binding.Hotkey);
                continue;
            }

            var index = Array.IndexOf(numberedQuickActionRecords, row.Key);
            if (index < 0)
                continue;
            var digit = Key._1 + index;
            var keypadDigit = Key.NumPad1 + index;
            if (IsNumberKeyAvailable(hintConfig, prefix, digit))
                row.QuickActionKeys = [new HistoryKeycap(digit)];
            else if (IsNumberKeyAvailable(hintConfig, prefix, keypadDigit))
                row.QuickActionKeys = [new HistoryKeycap(keypadDigit)];
        }
    }

    private static bool IsNumberKeyAvailable(HistoryShortcutConfig config, Hotkey prefix, Key digit)
    {
        var hotkey = new Hotkey([.. prefix.Keys, digit]);
        return config.Match(hotkey) is null && !config.RecordShortcuts.Any(binding => binding.Hotkey == hotkey);
    }

    private void ClearVisibleHints()
    {
        if (visibleQuickActionRecords is null)
            return;
        foreach (var record in visibleQuickActionRecords)
            record.QuickActionKeys = [];
        visibleQuickActionRecords = null;
    }

    partial void OnIsMultiSelectingChanged(bool value)
    {
        if (value)
            ClearQuickActionHints();
    }

    private void ClearQuickActionHints()
    {
        hintConfig = null;
        numberedQuickActionRecords = null;
        ClearVisibleHints();
    }

    private bool TryStartQuickAction(HistoryShortcutConfig config, Hotkey hotkey)
    {
        var target = config.RecordShortcuts.FirstOrDefault(binding => binding.Hotkey == hotkey)?.Record;
        if (target is null && numberedQuickActionRecords is { } records)
        {
            var prefix = config.GetShortcut(HistoryShortcutAction.QuickActionPrefix);
            var number = HistoryShortcutConfig.GetQuickActionNumber(prefix, hotkey);
            if (number > 0 && number <= records.Length)
                target = records[number - 1];
        }
        if (!target.HasValue)
            return false;
        if (ActiveQuickAction is not { IsCompleted: false })
            ActiveQuickAction = RunQuickActionAsync(config.QuickAction, target.Value, hotkey);
        return true;
    }

    public void UpdateQuickActionModifiers(Hotkey modifiers)
    {
        if (HasModalOperation || IsMultiSelecting || modifiers == Hotkey.Nothing)
            ClearQuickActionHints();
        else
            UpdateQuickActionPrefix(_configManager.GetConfig<HistoryShortcutConfig>(), modifiers);
    }

    private async Task RunQuickActionAsync(HistoryQuickAction action, HistoryRecordKey target, Hotkey hotkey)
    {
        try
        {
            var paste = action == HistoryQuickAction.Paste && TryReleasePasteShortcut(hotkey);
            var record = await RunWithOperationTimeoutAsync("load history record",
                token => historyManager.GetHistoryRecord(target.Hash, target.Type, token), canceledResult: null);
            if (record is { IsDeleted: false })
                await HandleCopyButtonAsync(new HistoryRecordVM(record), paste);
        }
        catch (Exception ex)
        {
            await logger.WriteAsync("Failed to run history quick action:", ex.Message);
        }
    }

    private void RemoveRecordShortcut(HistoryRecord record)
    {
        var key = HistoryRecordKey.From(record);
        _ = _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            var config = _configManager.GetConfig<HistoryShortcutConfig>();
            if (config.RecordShortcuts.Any(binding => binding.Record == key))
                _configManager.SetConfig(config.SetRecordShortcut(key, Hotkey.Nothing));
        });
    }

    public HistoryRecordShortcutEditor CreateRecordShortcutEditor(HistoryRecordKey record) => new(_configManager, historyManager, record);
}
