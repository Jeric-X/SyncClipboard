using SyncClipboard.Core.Interfaces;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SyncClipboard.Core.Commons
{
    public class UserConfig2
    {
        public event Action? ConfigChanged;

        private readonly ILogger _logger;
        private readonly IContextMenu _contextMenu;
        private readonly IAppConfig _appConfig;
        private readonly string _path;

        private delegate void ConfigChangedHandler(object obj);
        private readonly Dictionary<string, Type> _registedTypeList = new();
        private readonly Dictionary<string, HashSet<Action<object?>>> _registedChangedHandlerList = new();

        JsonNode _jsonNode = new JsonObject();

        public UserConfig2(ILogger logger, IAppConfig appConfig, IContextMenu contextMenu)
        {
            _logger = logger;
            _appConfig = appConfig;
            _contextMenu = contextMenu;
            _path = appConfig.UserConfigFile /*+ ".json"*/;
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

            _jsonNode[key] = JsonSerializer.SerializeToNode(newValue);
            NotifyRegistedHandler(key, newValue);
            ConfigChanged?.Invoke();
            Save();
        }

        private void NotifyRegistedHandler(string key, object? newValue)
        {
            foreach (var handler in _registedChangedHandlerList[key])
            {
                handler(newValue);
            }
        }

        private void NotifyAllRegistedHandler()
        {
            foreach (var handler in _jsonNode.AsObject())
            {
                if (_registedTypeList.ContainsKey(handler.Key))
                {
                    NotifyRegistedHandler(handler.Key, handler.Value.Deserialize(_registedTypeList[handler.Key]));
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
                _jsonNode = new JsonObject
                {
                    { "Program", JsonSerializer.SerializeToNode(_appConfig.ProgramWideUserConfig) }
                };
                Save();
            }
            NotifyAllRegistedHandler();
            ConfigChanged?.Invoke();
        }

        [SupportedOSPlatform("windows")]
        public void AddMenuItems()
        {
            MenuItem[] menuItems =
            {
                new MenuItem(
                    "打开配置文件", () => {
                        var open = new System.Diagnostics.Process();
                        open.StartInfo.FileName = "notepad";
                        open.StartInfo.Arguments = _path;
                        open.Start();
                    } )  ,
                new MenuItem(
                    "打开配置文件所在位置", () => {
                        var open = new System.Diagnostics.Process();
                        open.StartInfo.FileName = "explorer";
                        open.StartInfo.Arguments = "/e,/select," + _path;
                        open.Start();
                    }),
                new MenuItem("重新载入配置文件", () => this.Load())
            };
            _contextMenu.AddMenuItemGroup(menuItems);
        }
    }
}