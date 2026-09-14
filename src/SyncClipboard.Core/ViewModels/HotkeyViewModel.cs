using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public partial class HotkeyViewModel : ObservableObject
{
    [ObservableProperty]
    public partial ReadOnlyCollection<CommandCollectionViewModel>? CommandCollections { get; set; }

    [ObservableProperty]
    public partial Hotkey EditingHotkey { get; set; } = Hotkey.Nothing;

    partial void OnEditingHotkeyChanged(Hotkey value)
    {
        var copyHotkey = new Hotkey(OperatingSystem.IsMacOS() ? Key.Meta : Key.Ctrl, Key.C);
        if (value == copyHotkey)
        {
            IsEditingHasError = true;
            return;
        }

        IsEditingHasError = !_hotkeyManager.IsValidHotkeyForm(value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetHotkeyCanExecute))]
    [NotifyCanExecuteChangedFor(nameof(SetHotkeyCommand))]
    public partial bool IsEditingHasError { get; set; } = false;

    public bool SetHotkeyCanExecute => !IsEditingHasError;

    [ObservableProperty]
    public partial string EditingCmdId { get; set; } = string.Empty;

    [RelayCommand(CanExecute = nameof(SetHotkeyCanExecute))]
    private void SetHotkey()
    {
        _hotkeyManager.SetHotKey(EditingCmdId, EditingHotkey);
    }

    [RelayCommand]
    private void ClearEditingHotkey() => EditingHotkey = Hotkey.Nothing;

    [RelayCommand]
    private void SetToDefault(string cmdId) => _hotkeyManager.SetHotKeyToDefault(cmdId);

    private readonly HotkeyManager _hotkeyManager;
    private readonly MainViewModel _mainViewModel;

    [RelayCommand]
    private void OpenBlacklist() => _mainViewModel.NavigateToNextLevel(PageDefinition.HotkeyBlacklist);

    public HotkeyViewModel(HotkeyManager hotkeyManager, MainViewModel mainViewModel)
    {
        _hotkeyManager = hotkeyManager;
        _mainViewModel = mainViewModel;
        hotkeyManager.HotkeyStatusChanged += HotkeyStatusChanged;
        HotkeyStatusChanged();
    }

    private void HotkeyStatusChanged()
    {
        var collectionList = new List<CommandCollectionViewModel>();
        foreach (var collection in _hotkeyManager.CommandCollections)
        {
            var commandList = new List<UniqueCommandViewModel>();
            foreach (var command in collection.Commands)
            {
                var status = _hotkeyManager.HotkeyStatusMap[command.CmdId];
                var isError = status.Hotkey is not null && !status.IsReady;
                commandList.Add(new UniqueCommandViewModel(command.Name, command.CmdId, isError, status.Hotkey ?? Hotkey.Nothing));
            }
            collectionList.Add(new(collection.Name, collection.FontIcon, commandList));
        }
        CommandCollections = new(collectionList);
    }
}
