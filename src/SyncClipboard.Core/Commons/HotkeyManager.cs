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
    public ReadOnlyDictionary<Guid, HotkeyStatus> HotkeyStatusMap { get; }
    public event Action? HotkeyStatusChanged;

    public HotkeyManager(INativeHotkeyRegistry nativeHotkeyRegistry, ConfigManager configManager)
    {
        CommandCollections = _commandCollections.AsReadOnly();
        HotkeyStatusMap = new ReadOnlyDictionary<Guid, HotkeyStatus>(_hotkeyCommandMap);

        _nativeHotkeyRegistry = nativeHotkeyRegistry;
        _configManager = configManager;

        _configManager.GetAndListenConfig<HotkeyConfig>(ConfigChanged);
    }

    private bool RegisterToNative(Hotkey hotkey, Action action)
    {
        if (hotkey.Keys.Length is 0)
        {
            return true;
        }
        return _nativeHotkeyRegistry.RegisterForSystemHotkey(hotkey, action);
    }

    private void UnRegisterFromNative(Hotkey hotkey)
    {
        if (hotkey.Keys.Length is not 0)
        {
            _nativeHotkeyRegistry.UnRegisterForSystemHotkey(hotkey);
        }
    }

    private List<UniqueCommand> DeleteHotkeyCommandMap(IEnumerable<Guid> guids)
    {
        List<UniqueCommand> registedCommands = new();
        foreach (var guid in guids)
        {
            var status = _hotkeyCommandMap[guid];
            if (status.IsReady)
            {
                UnRegisterFromNative(status.Hotkey!);
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
                UnRegisterFromNative(hotkeyStatus.Hotkey!);
                hotkeyStatus.IsReady = false;
            }
            if (hotkeyStatus.Command is not null)
            {
                hotkeyStatus.IsReady = RegisterToNative(hotkey, hotkeyStatus.Command.Command);
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
        var oldHotkeys = _hotkeyConfig.Hotkeys.Except(config.Hotkeys);
        var newHotkeys = config.Hotkeys.Except(_hotkeyConfig.Hotkeys);
        _hotkeyConfig = config;

        List<UniqueCommand> registedCommands = DeleteHotkeyCommandMap(oldHotkeys.Select(pair => pair.Key));
        AddHotkeyCommandMap(newHotkeys);
        RegisterCommands(registedCommands);
        HotkeyStatusChanged?.Invoke();
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
                    ready = RegisterToNative(command.Hotkey, command.Command);
                }
                _hotkeyCommandMap.Add(command.Guid, new(command.Hotkey, ready, command));
            }
            else
            {
                hotkeyStatus.Command = command;
                if (hotkeyStatus.Hotkey is not null && hotkeyStatus.IsReady is false)
                {
                    var res = RegisterToNative(hotkeyStatus.Hotkey, command.Command);
                    hotkeyStatus.IsReady = res;
                }
            }
        }
        HotkeyStatusChanged?.Invoke();
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
            _hotkeyConfig.Hotkeys[guid] = hotkey;
        }
        else
        {
            _hotkeyConfig.Hotkeys.Add(guid, hotkey);
        }

        SetHotkeyCommandMap(guid, hotkey);
        HotkeyStatusChanged?.Invoke();
        _configManager.SetConfig(_hotkeyConfig);
    }

    public void SetHotKeyToDefault(Guid guid)
    {
        if (!_hotkeyCommandMap.TryGetValue(guid, out HotkeyStatus? status))
        {
            return;
        }

        if (status.Hotkey != status.Command?.Hotkey)
        {
            if (status.IsReady)
            {
                UnRegisterFromNative(status.Hotkey!);
            }
            status.IsReady = false;
            status.Hotkey = status.Command?.Hotkey;
        }

        if (status.Command is not null && status.Hotkey is not null && status.IsReady is false)
        {
            status.IsReady = RegisterToNative(status.Hotkey, status.Command.Command);
        }

        _hotkeyConfig.Hotkeys.Remove(guid);

        HotkeyStatusChanged?.Invoke();
        _configManager.SetConfig(_hotkeyConfig);
    }

    public bool IsValidHotkeyForm(Hotkey hotkey)
    {
        return hotkey == Hotkey.Nothing || _nativeHotkeyRegistry.IsValidHotkeyForm(hotkey);
    }
}
