using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public partial class HistorySettingViewModel
{
    public IReadOnlyList<HistoryShortcutSetting> ShortcutSettings { get; } = Enum.GetValues<HistoryShortcutAction>()
        .Select(action => new HistoryShortcutSetting(action, HistoryShortcutDescriptions.GetActionName(action))).ToArray();

    public IReadOnlyList<LocaleString<HistoryMouseAction>> MouseActions { get; } = Enum.GetValues<HistoryMouseAction>()
        .Select(action => new LocaleString<HistoryMouseAction>(action, HistoryShortcutDescriptions.GetMouseActionName(action)))
        .ToArray();

    private bool updatingShortcuts;
    private HistoryShortcutAction editingAction;

    [ObservableProperty]
    private LocaleString<HistoryMouseAction> doubleClickAction = null!;
    [ObservableProperty]
    private LocaleString<HistoryMouseAction> middleClickAction = null!;

    partial void OnDoubleClickActionChanged(LocaleString<HistoryMouseAction> value)
    {
        if (!updatingShortcuts && value is not null)
            _configManager.SetConfig(_configManager.GetConfig<HistoryShortcutConfig>() with { DoubleClickAction = value.Key });
    }

    partial void OnMiddleClickActionChanged(LocaleString<HistoryMouseAction> value)
    {
        if (!updatingShortcuts && value is not null)
            _configManager.SetConfig(_configManager.GetConfig<HistoryShortcutConfig>() with { MiddleClickAction = value.Key });
    }

    [ObservableProperty]
    private Hotkey editingShortcut = Hotkey.Nothing;
    partial void OnEditingShortcutChanged(Hotkey value) => ValidateEditingShortcut();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutHasError))]
    [NotifyPropertyChangedFor(nameof(CanSaveShortcut))]
    [NotifyCanExecuteChangedFor(nameof(SaveShortcutCommand))]
    private string shortcutErrorMessage = string.Empty;
    public bool ShortcutHasError => !string.IsNullOrEmpty(ShortcutErrorMessage);
    public bool CanSaveShortcut => !ShortcutHasError;

    private void InitializeShortcuts()
    {
        RefreshShortcuts(_configManager.GetConfig<HistoryShortcutConfig>());
        _configManager.ListenConfig<HistoryShortcutConfig>(RefreshShortcuts);
    }

    private void RefreshShortcuts(HistoryShortcutConfig config)
    {
        updatingShortcuts = true;
        try
        {
            foreach (var setting in ShortcutSettings)
                setting.Hotkey = config.GetShortcut(setting.Action);
            DoubleClickAction = LocaleString<HistoryMouseAction>.Match(MouseActions, config.DoubleClickAction);
            MiddleClickAction = LocaleString<HistoryMouseAction>.Match(MouseActions, config.MiddleClickAction);
            ValidateEditingShortcut();
        }
        finally
        {
            updatingShortcuts = false;
        }
    }

    public void BeginEditShortcut(HistoryShortcutSetting setting)
    {
        editingAction = setting.Action;
        EditingShortcut = setting.Hotkey;
        ValidateEditingShortcut();
    }

    private void ValidateEditingShortcut()
    {
        var (error, conflictingAction) = _configManager.GetConfig<HistoryShortcutConfig>().Validate(editingAction, EditingShortcut);
        var keys = string.Join("+", EditingShortcut.Keys.Select(key => key.ToEnumMemberValue()));
        ShortcutErrorMessage = error switch
        {
            HistoryShortcutError.None => string.Empty,
            HistoryShortcutError.Reserved => string.Format(Strings.HistoryShortcutReserved, keys),
            HistoryShortcutError.ModifierOnly => Strings.HistoryShortcutModifierOnly,
            HistoryShortcutError.MultipleMainKeys => Strings.HistoryShortcutMultipleMainKeys,
            HistoryShortcutError.SingleInputCharacter => string.Format(Strings.HistoryShortcutInputCharacter, keys),
            HistoryShortcutError.Conflict => string.Format(Strings.HistoryShortcutConflict,
                keys, HistoryShortcutDescriptions.GetActionName(conflictingAction!.Value)),
            _ => throw new ArgumentOutOfRangeException(nameof(error))
        };
    }

    [RelayCommand(CanExecute = nameof(CanSaveShortcut))]
    private void SaveShortcut()
    {
        ValidateEditingShortcut();
        if (ShortcutHasError)
            return;
        var config = _configManager.GetConfig<HistoryShortcutConfig>();
        var shortcuts = new Dictionary<HistoryShortcutAction, Hotkey>(config.Shortcuts)
        {
            [editingAction] = EditingShortcut
        };
        _configManager.SetConfig(config with { Shortcuts = shortcuts });
    }

    [RelayCommand]
    private void ResetHistoryShortcuts() =>
        _configManager.SetConfig(_configManager.GetConfig<HistoryShortcutConfig>() with { Shortcuts = [] });

    [RelayCommand]
    private async Task ResetShortcutAsync(HistoryShortcutSetting setting)
    {
        var config = _configManager.GetConfig<HistoryShortcutConfig>();
        var defaultHotkey = HistoryShortcutConfig.GetDefault(setting.Action);
        if (config.GetShortcut(setting.Action) == defaultHotkey)
            return;

        if (!config.CanAssign(setting.Action, defaultHotkey))
        {
            var conflictingSetting = ShortcutSettings.First(other =>
                other.Action != setting.Action && config.GetShortcut(other.Action) == defaultHotkey);
            var keys = string.Join("+", defaultHotkey.Keys.Select(key => key.ToEnumMemberValue()));
            await _dialog.ShowMessageAsync(Strings.ResetToDefault,
                string.Format(Strings.HistoryShortcutResetConflict, keys, conflictingSetting.Name));
            return;
        }

        var shortcuts = new Dictionary<HistoryShortcutAction, Hotkey>(config.Shortcuts);
        shortcuts.Remove(setting.Action);
        _configManager.SetConfig(config with { Shortcuts = shortcuts });
    }
}

public partial class HistoryShortcutSetting(HistoryShortcutAction action, string name) : ObservableObject
{
    public HistoryShortcutAction Action { get; } = action;
    public string Name { get; } = name;
    public bool IsModified => Hotkey != HistoryShortcutConfig.GetDefault(Action);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    private Hotkey hotkey = Hotkey.Nothing;
}
