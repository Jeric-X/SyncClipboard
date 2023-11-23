using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SyncClipboard.Core.Commons
{
    public class ConfigManager
    {
        public event Action? ConfigChanged;

        private readonly ILogger _logger;
        private readonly IContextMenu _contextMenu;
        private readonly string _path;

        private delegate void ConfigChangedHandler(object obj);
        private readonly Dictionary<string, Type> _registedTypeList = new();
        private readonly Dictionary<string, HashSet<Action<object?>>> _registedChangedHandlerList = new();

        JsonNode _jsonNode = new JsonObject();

        public ConfigManager(ILogger logger, IContextMenu contextMenu)
        {
            _logger = logger;
            _contextMenu = contextMenu;
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

        public void ListenConfig<T>(string key, Action<object?> action)
        {
            RegistConfigType(key, typeof(T));
            if (_registedChangedHandlerList.ContainsKey(key))
            {
                _registedChangedHandlerList[key].Add(action);
            }
            else
            {
                _registedChangedHandlerList.Add(key, new HashSet<Action<object?>>() { action });
            }
        }

        public void ListenConfig<T>(Action<object?> action)
        {
            ListenConfig<T>(ConfigKey.GetKeyFromType<T>(), action);
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
                handler(jsonNode.Deserialize(type));
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

        public void AddMenuItems()
        {
            List<MenuItem> menuItems = new()
            {
                new MenuItem(
                    I18n.Strings.OpenConfigFile, OpenConfigFile),
                new MenuItem(I18n.Strings.ReloadConfigFile, () => this.Load())
            };

            if (OperatingSystem.IsWindows())
            {
                menuItems.Add(new MenuItem(I18n.Strings.OpenConfigFileFolder, OpenConfigFileFolder));
            }
            _contextMenu.AddMenuItemGroup(menuItems.ToArray());
        }

        private void OpenConfigFile()
        {
            if (OperatingSystem.IsWindows())
            {
                var open = new System.Diagnostics.Process();
                open.StartInfo.FileName = "notepad";
                open.StartInfo.Arguments = _path;
                open.Start();
            }

            Sys.OpenWithDefaultApp(_path);
        }

        [SupportedOSPlatform("windows")]
        private void OpenConfigFileFolder()
        {
            var open = new System.Diagnostics.Process();
            open.StartInfo.FileName = "explorer";
            open.StartInfo.Arguments = "/e,/select," + _path;
            open.Start();
        }
    }
}