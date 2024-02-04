using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.Commons;

public record class HotkeyStatus
{
    public Hotkey? Hotkey { get; set; } = null;
    public bool IsReady { get; set; } = false;
    public UniqueCommand? Command { get; set; } = null;
    public HotkeyStatus(Hotkey? hotkey, bool isReady = false)
    {
        Hotkey = hotkey;
        IsReady = isReady;
    }
}

public class HotkeyManager
{
    private readonly INativeHotkeyRegistry _nativeHotkeyRegistry;
    private readonly ConfigManager _configManager;
    private readonly List<UniqueCommandCollection> _commandCollections = new List<UniqueCommandCollection>();
    private readonly Dictionary<Guid, HotkeyStatus> _hotkeyCommandMap = new();

    private HotkeyConfig _hotkeyConfig = new();

    public ReadOnlyCollection<UniqueCommandCollection> CommandCollections { get; }
    public ReadOnlyDictionary<Guid, HotkeyStatus> HotkeyCommandMap { get; }

    public HotkeyManager(INativeHotkeyRegistry nativeHotkeyRegistry, ConfigManager configManager)
    {
        CommandCollections = _commandCollections.AsReadOnly();
        HotkeyCommandMap = new ReadOnlyDictionary<Guid, HotkeyStatus>(_hotkeyCommandMap);

        _nativeHotkeyRegistry = nativeHotkeyRegistry;
        _configManager = configManager;

        _configManager.GetAndListenConfig<HotkeyConfig>(ConfigChanged);
    }

    private List<UniqueCommand> UnRegisterForOldHotkeys(IEnumerable<KeyValuePair<Guid, Hotkey>> oldHotkeys)
    {
        List<UniqueCommand> defaultHotkeys = new();
        foreach (var (guid, _) in oldHotkeys)
        {
            var status = _hotkeyCommandMap[guid];
            if (status.IsReady)
            {
                _nativeHotkeyRegistry.UnRegisterForSystemHotkey(status.Hotkey!);
                status.IsReady = false;
            }
            if (status.Command is null)
            {
                _hotkeyCommandMap.Remove(guid);
            }
            else if (status.Command.Hotkey is not null)
            {
                defaultHotkeys.Add(status.Command);
            }
        }
        return defaultHotkeys;
    }

    private void RegisterForNewHotkeys(IEnumerable<KeyValuePair<Guid, Hotkey>> newHotkeys)
    {
        foreach (var (guid, hotkey) in newHotkeys)
        {
            var found = _hotkeyCommandMap.TryGetValue(guid, out HotkeyStatus? hotkeyStatus);
            if (hotkeyStatus is not null)
            {
                if (hotkeyStatus.IsReady)
                {
                    _nativeHotkeyRegistry.UnRegisterForSystemHotkey(hotkeyStatus.Hotkey!);
                    hotkeyStatus.IsReady = false;
                }
                if (hotkeyStatus.Command is not null)
                {
                    var res = _nativeHotkeyRegistry.RegisterForSystemHotkey(hotkey, hotkeyStatus.Command.Command);
                    hotkeyStatus.IsReady = res;
                    hotkeyStatus.Hotkey = hotkey;
                }
            }
            else
            {
                _hotkeyCommandMap.Add(guid, new(hotkey));
            }
        }
    }

    private void RegisterForDefaultHotkeys(IEnumerable<UniqueCommand> commandsWithDefault)
    {
        foreach (var command in commandsWithDefault)
        {
            var ready = _nativeHotkeyRegistry.RegisterForSystemHotkey(command.Hotkey!, command.Command);
            _hotkeyCommandMap[command.Guid].Hotkey = command.Hotkey;
            _hotkeyCommandMap[command.Guid].IsReady = ready;
        }
    }

    private void ConfigChanged(HotkeyConfig config)
    {
        var sameHotkeys = _hotkeyConfig.Hotkeys.Intersect(config.Hotkeys);
        var oldHotkeys = sameHotkeys.ExceptBy(_hotkeyConfig.Hotkeys.Keys, (keyValuePair) => keyValuePair.Key);
        var newHotkeys = sameHotkeys.ExceptBy(config.Hotkeys.Keys, (keyValuePair) => keyValuePair.Key);
        _hotkeyConfig = config;

        List<UniqueCommand> commandWithDefault = UnRegisterForOldHotkeys(oldHotkeys);
        RegisterForNewHotkeys(newHotkeys);
        RegisterForDefaultHotkeys(commandWithDefault);
    }

    public void RegisterCommands(UniqueCommandCollection commandCollection)
    {
        _commandCollections.Add(commandCollection);
        foreach (var command in commandCollection.Commands)
        {
            _hotkeyCommandMap.TryGetValue(command.Guid, out HotkeyStatus? hotkeyStatus);
            if (hotkeyStatus is null)
            {
                bool ready = false;
                if (command.Hotkey is not null)
                {
                    ready = _nativeHotkeyRegistry.RegisterForSystemHotkey(command.Hotkey, command.Command);
                }
                _hotkeyCommandMap.Add(command.Guid, new(command.Hotkey, ready));
            }
            else if (hotkeyStatus.Hotkey is not null && hotkeyStatus.IsReady is false)
            {
                var res = _nativeHotkeyRegistry.RegisterForSystemHotkey(hotkeyStatus.Hotkey, command.Command);
                _hotkeyCommandMap[command.Guid].IsReady = res;
            }
        }
    }

#pragma warning disable CA1822 // 将成员标记为 static
#pragma warning disable IDE0060 // 删除未使用的参数
    public void SetHotKey(Guid guid, Hotkey hotkey)
#pragma warning restore IDE0060 // 删除未使用的参数
#pragma warning restore CA1822 // 将成员标记为 static
    {
    }
}
