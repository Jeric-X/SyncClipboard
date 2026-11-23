using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryRecordShortcutEditor : ObservableObject
{
    private readonly ConfigManager configManager;
    private readonly HistoryRecordKey record;

    public HistoryRecordShortcutEditor(ConfigManager configManager, HistoryRecordKey record)
    {
        this.configManager = configManager;
        this.record = record;
        EditingShortcut = configManager.GetConfig<HistoryShortcutConfig>().RecordShortcuts
            .FirstOrDefault(binding => binding.Record == record)?.Hotkey ?? Hotkey.Nothing;
    }

    [ObservableProperty]
    private Hotkey editingShortcut = Hotkey.Nothing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(HasError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string errorMessage = string.Empty;

    public bool HasError => !CanSave;
    public bool CanSave => ErrorMessage.Length == 0;

    partial void OnEditingShortcutChanged(Hotkey value) => Validate(configManager.GetConfig<HistoryShortcutConfig>());

    private void Validate(HistoryShortcutConfig config)
    {
        var (error, action) = config.ValidateRecordShortcut(record, EditingShortcut);
        ErrorMessage = HistoryShortcutDescriptions.GetErrorMessage(EditingShortcut, error, action);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var config = configManager.GetConfig<HistoryShortcutConfig>();
        Validate(config);
        if (CanSave)
            configManager.SetConfig(config.WithRecordShortcut(record, EditingShortcut));
    }
}
