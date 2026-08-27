using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.Runtime)]
public record class RuntimeHistoryConfig
{
    public const string ConfigKey = "RuntimeHistory";

    public bool EnableSyncHistory { get; set; } = false;
}
