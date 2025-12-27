namespace SyncClipboard.Core.Models.UserConfigs;

public record class RuntimeHistoryConfig
{
    public bool EnableSyncHistory { get; set; } = false;
}