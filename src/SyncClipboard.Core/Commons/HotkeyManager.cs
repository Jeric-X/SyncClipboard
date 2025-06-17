using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.Commons;

public class HotkeyManager
{
    private readonly INativeHotkeyRegistry _nativeHotkeyRegistry;
    private readonly ConfigManager _configManager;
    private readonly List<UniqueCommandCollection> _commandCollections = [];
    private readonly Dictionary<string, HotkeyStatus> _hotkeyCommandMap = [];

    private HotkeyConfig _hotkeyConfig = new();

    public ReadOnlyCollection<UniqueCommandCollection> CommandCollections { get; }
    public ReadOnlyDictionary<string, HotkeyStatus> HotkeyStatusMap { get; }
    public event Action? HotkeyStatusChanged;

    public HotkeyManager(INativeHotkeyRegistry nativeHotkeyRegistry, ConfigManager configManager)
    {
        CommandCollections = _commandCollections.AsReadOnly();
        HotkeyStatusMap = new ReadOnlyDictionary<string, HotkeyStatus>(_hotkeyCommandMap);

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

        if (!_nativeHotkeyRegistry.IsValidHotkeyForm(hotkey))
        {
            return false;
        }
        return _nativeHotkeyRegistry.RegisterForSystemHotkey(hotkey, action.NoExcept());
    }

    private void UnRegisterFromNative(Hotkey hotkey)
    {
        if (hotkey.Keys.Length is not 0)
        {
            _nativeHotkeyRegistry.UnRegisterForSystemHotkey(hotkey);
        }
    }

    private List<UniqueCommand> DeleteHotkeyCommandMap(IEnumerable<string> ids)
    {
        List<UniqueCommand> registedCommands = [];
        foreach (var guid in ids)
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

    private void SetHotkeyCommandMap(string id, Hotkey hotkey)
    {
        var found = _hotkeyCommandMap.TryGetValue(id, out HotkeyStatus? hotkeyStatus);
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
            _hotkeyCommandMap.Add(id, new(hotkey));
        }
    }

    private void AddHotkeyCommandMap(IEnumerable<KeyValuePair<string, Hotkey>> hotkeys)
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

    private void RegisterCommands(IEnumerable<UniqueCommand> commands)
    {
        commands.ForEach(command => RegisterCommand(command, false));
        HotkeyStatusChanged?.Invoke();
    }

    private void RegisterCommand(UniqueCommand command, bool notify = true)
    {
        _hotkeyCommandMap.TryGetValue(command.CmdId, out HotkeyStatus? hotkeyStatus);
        if (hotkeyStatus is null)
        {
            bool ready = false;
            if (command.Hotkey is not null)
            {
                ready = RegisterToNative(command.Hotkey, command.Command);
            }
            _hotkeyCommandMap.Add(command.CmdId, new(command.Hotkey, ready, command));
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

        if (notify)
            HotkeyStatusChanged?.Invoke();
    }

    public void RegisterCommands(UniqueCommandCollection commandCollection)
    {
        var collection = _commandCollections.Find(collection => collection.Name == commandCollection.Name);
        if (collection is null)
            _commandCollections.Add(commandCollection);
        else
            commandCollection.Commands.ForEach(collection.Commands.Add);

        RegisterCommands(commandCollection.Commands);
    }

    public void SetHotKey(string id, Hotkey hotkey)
    {
        _hotkeyConfig.Hotkeys.TryGetValue(id, out var existHotkey);
        if (existHotkey is not null)
        {
            _hotkeyConfig.Hotkeys[id] = hotkey;
        }
        else
        {
            _hotkeyConfig.Hotkeys.Add(id, hotkey);
        }

        SetHotkeyCommandMap(id, hotkey);
        HotkeyStatusChanged?.Invoke();
        _configManager.SetConfig(_hotkeyConfig);
    }

    public void SetHotKeyToDefault(string id)
    {
        if (!_hotkeyCommandMap.TryGetValue(id, out HotkeyStatus? status))
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

        _hotkeyConfig.Hotkeys.Remove(id);

        HotkeyStatusChanged?.Invoke();
        _configManager.SetConfig(_hotkeyConfig);
    }

    public bool IsValidHotkeyForm(Hotkey hotkey)
    {
        return hotkey == Hotkey.Nothing || _nativeHotkeyRegistry.IsValidHotkeyForm(hotkey);
    }

    public void RunCommand(string cmdId)
    {
        if (_hotkeyCommandMap.TryGetValue(cmdId, out HotkeyStatus? status) && status.Command is not null)
        {
            status.Command.Command.InvokeNoExcept();
        }
    }
}
