using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SyncClipboard.Core.Commons
{
    public class ConfigManager
    {
        public event Action? ConfigChanged;

        private readonly ILogger _logger;
        private readonly string _path;

        private readonly Dictionary<string, Type> _registedTypeList = new();
        private readonly Dictionary<string, HashSet<MessageDispatcher>> _registedChangedHandlerList = new();

        JsonNode _jsonNode = new JsonObject();

        public ConfigManager(ILogger logger)
        {
            _logger = logger;
            _path = Env.UserConfigFile;
            Load();
        }

        public T? GetConfig<T>(string key)
        {
            var node = _jsonNode[key];
            if (node is null)
            {
                return default;
            }

            return node.Deserialize<T>();
        }

        public T GetConfig<T>() where T : new()
        {
            return GetConfig<T>(ConfigKey.GetKeyFromType<T>()) ?? new();
        }

        public void RegistConfigType(string key, Type type)
        {
            if (_registedTypeList.Contains(new KeyValuePair<string, Type>(key, type)))
            {
                return;
            }
            else if (_registedTypeList.ContainsKey(key))
            {
                throw new Exception("Key & type pair are not same as existed.");
            }

            _registedTypeList.Add(key, type);
        }

        public void ListenConfig<T>(string key, MessageHandler<T> action)
        {
            RegistConfigType(key, typeof(T));
            if (_registedChangedHandlerList.ContainsKey(key))
            {
                _registedChangedHandlerList[key].Add(new MessageDispatcher.For<T>(action));
            }
            else
            {
                _registedChangedHandlerList.Add(key, new HashSet<MessageDispatcher>() { new MessageDispatcher.For<T>(action) });
            }
        }

        public void ListenConfig<T>(MessageHandler<T> action)
        {
            ListenConfig(ConfigKey.GetKeyFromType<T>(), action);
        }

        public void SetConfig<T>(string key, T newValue) where T : IEquatable<T>
        {
            ArgumentNullException.ThrowIfNull(newValue);
            RegistConfigType(key, typeof(T));

            var exist = _jsonNode[key];
            if (exist is not null)
            {
                var oldValue = exist.Deserialize<T>();
                if (newValue.Equals(oldValue))
                {
                    return;
                }
            }

#if DEBUG
            _logger.Write("[Writting Config] " + newValue.ToString() ?? "");
#endif

            _jsonNode[key] = JsonSerializer.SerializeToNode(newValue);
            NotifyRegistedHandler(key, typeof(T), _jsonNode[key]);
            ConfigChanged?.Invoke();
            Save();
        }

        public void SetConfig<T>(T newValue) where T : IEquatable<T>
        {
            SetConfig(ConfigKey.GetKeyFromType<T>(), newValue);
        }

        private void NotifyRegistedHandler(string key, Type type, JsonNode? jsonNode)
        {
            if (!_registedChangedHandlerList.ContainsKey(key))
            {
                return;
            }
            foreach (var handler in _registedChangedHandlerList[key])
            {
                var obj = jsonNode.Deserialize(type);
                if (obj is not null)
                {
                    handler.Invoke(obj);
                }
            }
        }

        private void NotifyAllRegistedHandler()
        {
            foreach (var configNode in _jsonNode.AsObject())
            {
                if (_registedTypeList.ContainsKey(configNode.Key))
                {
                    NotifyRegistedHandler(configNode.Key, _registedTypeList[configNode.Key], configNode.Value);
                }
            }
        }

        private void Save()
        {
            var jsonString = _jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, jsonString);
        }

        public void Load()
        {
            try
            {
                var text = File.ReadAllText(_path);
                _jsonNode = JsonNode.Parse(text) ?? new JsonObject();
            }
            catch
            {
                _jsonNode = new JsonObject();
                Save();
            }
            NotifyAllRegistedHandler();
            ConfigChanged?.Invoke();
        }

        public MenuItem[] Menu => new MenuItem[]
        {
#if MACOS
            new MenuItem(I18n.Strings.OpenConfigFile, () => Process.Start("open", $"-a TextEdit \"{_path}\"")),
            new MenuItem(I18n.Strings.ReloadConfigFile, Load),
            new MenuItem(I18n.Strings.OpenConfigFileFolder, () => Process.Start("open", $"-R \"{_path}\""))
#endif
#if WINDOWS     
            new MenuItem(I18n.Strings.OpenConfigFile, () => Process.Start("notepad", _path)),
            new MenuItem(I18n.Strings.ReloadConfigFile, Load),
            new MenuItem(I18n.Strings.OpenConfigFileFolder, () => Process.Start("explorer", $"/e,/select,{_path}"))
#endif
#if LINUX
            new MenuItem(I18n.Strings.OpenConfigFile, () => Sys.OpenWithDefaultApp(_path)),
            new MenuItem(I18n.Strings.ReloadConfigFile, Load),
#endif
        };
    }
}