namespace SyncClipboard.Core.Models.UserConfigs;

public record class CommandConfig
{
    public bool SwitchOn { get; set; } = false;
    public uint Shutdowntime { get; set; } = 30;
    public uint IntervalTime { get; set; } = 3;
}