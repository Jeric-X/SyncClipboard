namespace SyncClipboard.Core.Models.UserConfigs;

public record ChangeableAppConfig
{
    public int IntervalTime { get; set; } = 3;
    public int RetryTimes { get; set; } = 3;
    public int TimeOut { get; set; } = 100;
    public string Proxy { get; set; } = "";
    public bool DeleteTempFilesOnStartUp { get; set; } = false;
    public int LogRemainDays { get; set; } = 8;
};