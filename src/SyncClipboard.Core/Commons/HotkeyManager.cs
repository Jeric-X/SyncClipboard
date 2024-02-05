using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.Commons;

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

    private List<UniqueCommand> DeleteHotkeyCommandMap(IEnumerable<Guid> guids)
    {
        List<UniqueCommand> registedCommands = new();
        foreach (var guid in guids)
        {
            var status = _hotkeyCommandMap[guid];
            if (status.IsReady)
            {
                _nativeHotkeyRegistry.UnRegisterForSystemHotkey(status.Hotkey!);
                status.IsReady = false;
            }
            if (status.Command is not null)
            {
                registedCommands.Add(status.Command);
            }
            _hotkeyCommandMap.Remove(guid);
        }
        return registedCommands;
    }

    private void SetHotkeyCommandMap(Guid guid, Hotkey hotkey)
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

    private void AddHotkeyCommandMap(IEnumerable<KeyValuePair<Guid, Hotkey>> hotkeys)
    {
        foreach (var (guid, hotkey) in hotkeys)
        {
            SetHotkeyCommandMap(guid, hotkey);
        }
    }

    private void ConfigChanged(HotkeyConfig config)
    {
        var sameHotkeys = _hotkeyConfig.Hotkeys.Intersect(config.Hotkeys);
        var oldHotkeys = sameHotkeys.ExceptBy(_hotkeyConfig.Hotkeys.Keys, (keyValuePair) => keyValuePair.Key);
        var newHotkeys = sameHotkeys.ExceptBy(config.Hotkeys.Keys, (keyValuePair) => keyValuePair.Key);
        _hotkeyConfig = config;

        List<UniqueCommand> registedCommands = DeleteHotkeyCommandMap(oldHotkeys.Select(pair => pair.Key));
        AddHotkeyCommandMap(newHotkeys);
        RegisterCommands(registedCommands);
    }

    public void RegisterCommands(IEnumerable<UniqueCommand> commands)
    {
        foreach (var command in commands)
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

    public void RegisterCommands(UniqueCommandCollection commandCollection)
    {
        _commandCollections.Add(commandCollection);
        RegisterCommands(commandCollection.Commands);
    }

    public void SetHotKey(Guid guid, Hotkey hotkey)
    {
        _hotkeyConfig.Hotkeys.TryGetValue(guid, out var existHotkey);
        if (existHotkey is not null)
        {
            if (hotkey == existHotkey)
            {
                return;
            }
            _hotkeyConfig.Hotkeys[guid] = hotkey;
        }
        else
        {
            _hotkeyConfig.Hotkeys.Add(guid, hotkey);
        }

        SetHotkeyCommandMap(guid, hotkey);
        _configManager.SetConfig(_hotkeyConfig);
    }
}
