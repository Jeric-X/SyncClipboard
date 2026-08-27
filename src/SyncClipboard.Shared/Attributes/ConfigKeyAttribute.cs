namespace SyncClipboard.Shared.Attributes;

public enum ConfigStorage
{
    SyncClipboard,
    Static,
    Runtime,
    UpdateInfo,
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ConfigKeyAttribute(string key, ConfigStorage storage) : Attribute
{
    public string Key { get; } = key;

    public ConfigStorage Storage { get; } = storage;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class OptionalConfigKeyAttribute(string key, ConfigStorage storage) : Attribute
{
    public string Key { get; } = key;

    public ConfigStorage Storage { get; } = storage;
}
