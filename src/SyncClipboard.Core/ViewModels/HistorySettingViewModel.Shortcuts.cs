using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.ViewModels;

public partial class HistorySettingViewModel
{
    public IReadOnlyList<HistoryShortcutSetting> ShortcutSettings { get; } =
    [
        new(HistoryShortcutAction.Search, Strings.HistoryShortcutSearch),
        new(HistoryShortcutAction.PreviousItem, Strings.HistoryShortcutPreviousItem),
        new(HistoryShortcutAction.NextItem, Strings.HistoryShortcutNextItem),
        new(HistoryShortcutAction.FirstItem, Strings.HistoryShortcutFirstItem),
        new(HistoryShortcutAction.LastItem, Strings.HistoryShortcutLastItem),
        new(HistoryShortcutAction.ToggleStar, Strings.HistoryShortcutToggleStar),
        new(HistoryShortcutAction.ToggleStarredFilter, Strings.HistoryShortcutToggleStarredFilter),
        new(HistoryShortcutAction.ToggleTopmost, Strings.HistoryShortcutToggleTopmost),
        new(HistoryShortcutAction.Delete, Strings.HistoryShortcutDelete),
        new(HistoryShortcutAction.TogglePreview, Strings.HistoryShortcutTogglePreview),
        new(HistoryShortcutAction.NextFilter, Strings.HistoryShortcutNextFilter),
        new(HistoryShortcutAction.PreviousFilter, Strings.HistoryShortcutPreviousFilter),
        new(HistoryShortcutAction.CopyAndPaste, Strings.HistoryShortcutCopyAndPaste),
        new(HistoryShortcutAction.Copy, Strings.HistoryShortcutCopy),
        new(HistoryShortcutAction.ExecuteRecommendedAction, Strings.ExecuteRecommendedAction),
    ];

    public IReadOnlyList<LocaleString<HistoryMouseAction>> MouseActions { get; } =
    [
        new(HistoryMouseAction.Copy, Strings.HistoryShortcutCopy),
        new(HistoryMouseAction.CopyAndPaste, Strings.HistoryShortcutCopyAndPaste),
        new(HistoryMouseAction.ExecuteRecommendedAction, Strings.ExecuteRecommendedAction)
    ];

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
