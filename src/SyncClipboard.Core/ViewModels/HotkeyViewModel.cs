using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.Keyboard;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public record UniqueCommandViewModel(string Name, Guid Guid, bool IsError, Hotkey? Hotkey = null);

public record CommandCollectionViewModel(string Name, string FontIcon, List<UniqueCommandViewModel>? Commands);

public partial class HotkeyViewModel : ObservableObject
{
    [ObservableProperty]
    private ReadOnlyCollection<CommandCollectionViewModel>? commandCollections;

    private readonly HotkeyManager _hotkeyManager;

    public HotkeyViewModel(HotkeyManager hotkeyManager)
    {
        _hotkeyManager = hotkeyManager;
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
