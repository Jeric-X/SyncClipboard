using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryRecordShortcutEditor : ObservableObject
{
    private readonly ConfigManager configManager;
    private readonly HistoryManager historyManager;
    private readonly HistoryRecordKey record;

    public HistoryRecordShortcutEditor(ConfigManager configManager, HistoryManager historyManager, HistoryRecordKey record)
    {
        this.configManager = configManager;
        this.historyManager = historyManager;
        this.record = record;
        EditingShortcut = configManager.GetConfig<HistoryShortcutConfig>().RecordShortcuts
            .FirstOrDefault(binding => binding.Record == record)?.Hotkey ?? Hotkey.Nothing;
    }

    [ObservableProperty]
    private Hotkey editingShortcut = Hotkey.Nothing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(HasError))]
    private string errorMessage = string.Empty;

    public bool HasError => !CanSave;
    public bool CanSave => ErrorMessage.Length == 0;

    partial void OnEditingShortcutChanged(Hotkey value) => Validate(configManager.GetConfig<HistoryShortcutConfig>());

    private void Validate(HistoryShortcutConfig config)
    {
        var (error, action) = config.ValidateRecordShortcut(EditingShortcut);
        ErrorMessage = HistoryShortcutDescriptions.GetErrorMessage(EditingShortcut, error, action);
    }

    public async Task<bool> TrySaveAsync(IMainWindowDialog dialog)
    {
        var shortcut = EditingShortcut;
        var approved = new HashSet<HistoryRecordKey>();
        while (true)
        {
            var config = configManager.GetConfig<HistoryShortcutConfig>();
            Validate(config);
            if (!CanSave || EditingShortcut != shortcut)
                return false;

            var owner = config.RecordShortcuts.FirstOrDefault(binding => shortcut != Hotkey.Nothing
                && binding.Hotkey == shortcut && binding.Record != record && !approved.Contains(binding.Record));
            if (owner is null)
            {
                configManager.SetConfig(config.WithRecordShortcut(record, shortcut));
                return true;
            }

            var existing = await historyManager.GetHistoryRecord(owner.Record.Hash, owner.Record.Type, CancellationToken.None);
            if (existing is { IsDeleted: false })
            {
                var message = string.Format(Strings.HistoryShortcutReassignMessage,
                    HistoryShortcutDescriptions.Format(shortcut), GetPreview(existing));
                if (!await dialog.ShowConfirmationAsync(Strings.HistoryShortcutReassignTitle, message))
                    return false;
            }
            // Re-read the bindings after the lookup or prompt so a new owner requires its own confirmation.
            approved.Add(owner.Record);
        }
    }

    private static string GetPreview(HistoryRecord record)
    {
        var text = record.Text.ReplaceLineEndings(" ").Trim();
        const int maxLength = 160;
        if (text.Length > maxLength)
        {
            var end = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
            text = text[..end] + "…";
        }
        return string.IsNullOrWhiteSpace(record.Title) ? text : $"{record.Title}\n{text}".TrimEnd();
    }
}
