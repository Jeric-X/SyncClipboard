using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public record UniqueCommandViewModel(string Name, Guid Guid, bool IsError, Hotkey? Hotkey = null);

public record CommandCollectionViewModel(string Name, string FontIcon, List<UniqueCommandViewModel>? Commands);

public partial class HotkeyViewModel : ObservableObject
{
    [ObservableProperty]
    private ReadOnlyCollection<CommandCollectionViewModel>? commandCollections;

    [ObservableProperty]
    private Hotkey editingHotkey = Hotkey.Nothing;
    partial void OnEditingHotkeyChanged(Hotkey value)
    {
        IsEditingHasError = !_hotkeyManager.IsValidHotkeyForm(value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetHotkeyCanExecute))]
    [NotifyCanExecuteChangedFor(nameof(SetHotkeyCommand))]
    private bool isEditingHasError = false;

    public bool SetHotkeyCanExecute => !IsEditingHasError;

    [ObservableProperty]
    private Guid editingGuid;

    [RelayCommand(CanExecute = nameof(SetHotkeyCanExecute))]
    private void SetHotkey()
    {
        _hotkeyManager.SetHotKey(EditingGuid, EditingHotkey);
    }

    [RelayCommand]
    private void ClearEditingHotkey() => EditingHotkey = Hotkey.Nothing;

    [RelayCommand]
    private void SetToDefault(Guid guid) => _hotkeyManager.SetHotKeyToDefault(guid);

    private readonly HotkeyManager _hotkeyManager;
    private readonly ILogger _logger;

    public HotkeyViewModel(HotkeyManager hotkeyManager, ILogger logger)
    {
        _hotkeyManager = hotkeyManager;
        _logger = logger;
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
                var status = _hotkeyManager.HotkeyStatusMap[command.Guid];
                var isError = status.Hotkey is not null && !status.IsReady;
                commandList.Add(new UniqueCommandViewModel(command.Name, command.Guid, isError, status.Hotkey));
            }
            collectionList.Add(new(collection.Name, collection.FontIcon, commandList));
        }
        CommandCollections = new(collectionList);
    }
}
