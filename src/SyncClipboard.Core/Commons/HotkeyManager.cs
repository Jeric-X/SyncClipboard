using SyncClipboard.Core.Models;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.Commons;

public class HotkeyManager
{
    private readonly List<UniqueCommandCollection> _commandCollections = new List<UniqueCommandCollection>();

    public ReadOnlyCollection<UniqueCommandCollection> CommandCollections => _commandCollections.AsReadOnly();

    public void RegisterCommands(UniqueCommandCollection commandCollection)
    {
        _commandCollections.Add(commandCollection);
    }

#pragma warning disable CA1822 // 将成员标记为 static
#pragma warning disable IDE0060 // 删除未使用的参数
    public void SetHotKey(Guid guid, Hotkey hotkey)
#pragma warning restore IDE0060 // 删除未使用的参数
#pragma warning restore CA1822 // 将成员标记为 static
    {
    }
}
