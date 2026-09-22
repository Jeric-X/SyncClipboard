using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [NotifyPropertyChangedFor(nameof(CanSaveShortcut))]
    [NotifyCanExecuteChangedFor(nameof(SaveShortcutCommand))]
    private bool shortcutHasError;
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

    private void ValidateEditingShortcut() => ShortcutHasError =
        !_configManager.GetConfig<HistoryShortcutConfig>().CanAssign(editingAction, EditingShortcut);

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
    private void ResetHistoryShortcuts() => _configManager.SetConfig(new HistoryShortcutConfig());
}

public partial class HistoryShortcutSetting(HistoryShortcutAction action, string name) : ObservableObject
{
    public HistoryShortcutAction Action { get; } = action;
    public string Name { get; } = name;

    [ObservableProperty]
    private Hotkey hotkey = Hotkey.Nothing;
}
