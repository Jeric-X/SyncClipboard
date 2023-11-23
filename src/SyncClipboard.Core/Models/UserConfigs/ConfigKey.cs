namespace SyncClipboard.Core.Models.UserConfigs;

public static class ConfigKey
{
    public const string ClipboardAssist = "ClipboardAssist";
    public const string Sync = "SyncService";
    public const string Server = "ServerService";
    public const string Program = "Program";

    public static string GetKeyFromType<T>()
    {
        var configTypeName = typeof(T).Name;
        var tailLength = "Config".Length;
        var fieldName = configTypeName[..^tailLength];
        var key = typeof(ConfigKey).GetField(fieldName)?.GetValue(null) as string;
        ArgumentNullException.ThrowIfNull(key, nameof(key));
        return key;
    }
}
