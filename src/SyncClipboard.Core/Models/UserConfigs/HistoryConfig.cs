namespace SyncClipboard.Core.Models.UserConfigs;

public record class HistoryConfig
{
    public bool EnableHistory { get; set; } = false;
    public bool CloseWhenLostFocus { get; set; } = true;
    public uint MaxItemCount { get; set; } = 100;
}