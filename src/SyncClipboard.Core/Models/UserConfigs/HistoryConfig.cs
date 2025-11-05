namespace SyncClipboard.Core.Models.UserConfigs;

public record class HistoryConfig
{
    public bool EnableHistory { get; set; } = false;
    public bool EnableSyncHistory { get; set; } = false;
    public uint MaxItemCount { get; set; } = 100;
    public uint HistoryRetentionMinutes { get; set; } = 10080; // 7 days in minutes
}