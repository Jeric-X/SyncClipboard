using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public class HotkeyViewModel
{
    public ReadOnlyCollection<UniqueCommandCollection>? CommandCollections { get; }

    public HotkeyViewModel(HotkeyManager hotkeyManager)
    {
        CommandCollections = hotkeyManager.CommandCollections;
    }
}
