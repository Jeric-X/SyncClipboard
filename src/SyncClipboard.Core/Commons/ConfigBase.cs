using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SyncClipboard.Core.Commons
{
    public class ConfigBase(INotificationManager? notification = null)
    {
        public event Action? ConfigChanged;

        public string Path { get; protected set; } = null!;
        protected INotificationManager? NotificationManager { get; set; } = notification;

        private readonly Dictionary<string, Type> _registedTypeList = [];
        private readonly Dictionary<string, HashSet<MessageDispatcher>> _registedChangedHandlerList = [];

        private JsonNode _jsonNode = new JsonObject();
        private JsonNode _jsonNodeBackUp = new JsonObject();

        public ConfigBase(string path, INotificationManager? notificationManager = null) : this(notificationManager)
        {
            Path = path;
            Load();
        }

        public ConfigBase(string path, IServiceProvider sp) : this(sp.GetRequiredService<INotificationManager>())
        {
            Path = path;
            Load();
        }

        public T? GetConfig<T>(string key) where T : IEquatable<T>, new()
        {
            var node = _jsonNode[key];
            if (node is null)
            {
                //SetConfig<T>(key, new());
                return new();
            }

            return node.Deserialize<T>();
        }

        public T GetConfig<T>() where T : IEquatable<T>, new()
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

            if (_registedChangedHandlerList.TryGetValue(key, out HashSet<MessageDispatcher>? dispatchers))
            {
                dispatchers.Add(new MessageDispatcher.For<T>(action));
            }
            else
            {
                _registedChangedHandlerList.Add(key, [new MessageDispatcher.For<T>(action)]);
            }
        }

        public void ListenConfig<T>(MessageHandler<T> action)
        {
            ListenConfig(ConfigKey.GetKeyFromType<T>(), action);
        }

        public void GetAndListenConfig<T>(MessageHandler<T> action) where T : IEquatable<T>, new()
        {
            ListenConfig(ConfigKey.GetKeyFromType<T>(), action);
            action?.Invoke(GetConfig<T>());
        }

        public T GetListenConfig<T>(MessageHandler<T> action) where T : IEquatable<T>, new()
        {
            ListenConfig(ConfigKey.GetKeyFromType<T>(), action);
            return GetConfig<T>();
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
            Save();
            NotifyRegistedHandler(key, typeof(T), _jsonNode[key]);
            ConfigChanged?.Invoke();
        }

        public void SetConfig<T>(T newValue) where T : IEquatable<T>
        {
            SetConfig(ConfigKey.GetKeyFromType<T>(), newValue);
        }

        private void NotifyRegistedHandler(string key, Type type, JsonNode? jsonNode)
        {
            if (!_registedChangedHandlerList.TryGetValue(key, out HashSet<MessageDispatcher>? dispathers))
            {
                return;
            }

            var obj = jsonNode.Deserialize(type) ?? Activator.CreateInstance(type);
            foreach (var handler in dispathers)
            {
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
                if (_registedTypeList.TryGetValue(configNode.Key, out Type? type))
                {
                    NotifyRegistedHandler(configNode.Key, type, configNode.Value);
                }
            }
        }

        protected virtual void Save()
        {
            try
            {
                var jsonString = _jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path, jsonString);
                _jsonNodeBackUp = _jsonNode.DeepClone();
            }
            catch (Exception e)
            {
                NotificationManager?.ShowText("Failed to write config file", e.Message);
                AppCore.TryGetCurrent()?.Logger.Write($"Failed to write config file to {Path}, err message: {e.Message}");
                _jsonNode = _jsonNodeBackUp.DeepClone();
            }
        }

        public void Load()
        {
            try
            {
                var text = File.ReadAllText(Path);
                _jsonNode = JsonNode.Parse(text) ?? new JsonObject();
                _jsonNodeBackUp = _jsonNode.DeepClone();
            }
            catch
            {
                _jsonNode = new JsonObject();
                _jsonNodeBackUp = _jsonNode.DeepClone();
                //Save();
            }
            NotifyAllRegistedHandler();
            ConfigChanged?.Invoke();
        }
    }
}