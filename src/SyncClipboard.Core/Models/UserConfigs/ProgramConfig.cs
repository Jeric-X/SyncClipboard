namespace SyncClipboard.Core.Models.UserConfigs;

public record ProgramConfig
{
    public string Proxy { get; set; } = "";
    public bool DeleteTempFilesOnStartUp { get; set; } = true;
    public uint LogRemainDays { get; set; } = 8;
    public bool CheckUpdateOnStartUp { get; set; } = true;
    public string Language { get; set; } = "";
    public bool HideWindowOnStartup { get; set; } = true;
};