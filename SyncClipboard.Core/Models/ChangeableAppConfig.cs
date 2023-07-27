namespace SyncClipboard.Core.Models;

public record ChangeableAppConfig(
    int IntervalTime = 3,
    int RetryTimes = 3,
    int TimeOut = 100,
    string Proxy = "",
    bool DeleteTempFilesOnStartUp = false,
    int LogRemainDays = 8
);