namespace SyncClipboard.Server.Core.Models;

public sealed class AppSettings
{
    public bool ProtectTaggedRecords { get; set; } = true;
    public bool ProtectTitledRecords { get; set; } = true;
    public uint MaxSavedHistoryCount { get; set; } = 1000;
    public uint HistoryRetentionMinutes { get; set; } = 0;
}
