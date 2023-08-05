namespace SyncClipboard.Core.Models.UserConfigs;

public record ProgramConfig
{
    public string Proxy { get; set; } = "";
    public bool DeleteTempFilesOnStartUp { get; set; } = false;
    public int LogRemainDays { get; set; } = 8;
    public bool CheckUpdateOnStartUp { get; set; }
};