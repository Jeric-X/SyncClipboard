using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.Runtime)]
public sealed record HistoryNotificationConfig
{
    public const string ConfigKey = "HistoryNotification";

    public string[] IgnoredTagSyncServers { get; init; } = [];
}
