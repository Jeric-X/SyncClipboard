using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record class HistoryConfig
{
    public const string ConfigKey = "History";

    public bool EnableHistory { get; set; } = false;
    public bool EnableSyncHistory { get; set; } = false;
    public bool AutoDeleteMissingLocalFiles { get; set; } = false;
    public uint MaxItemCount { get; set; } = 100;
    public uint HistoryRetentionMinutes { get; set; } = 10080; // 7 days in minutes
}
