namespace SyncClipboard.Core.Models.UserConfigs;

public record class LinuxRuntimeConfig
{
    public string AppImageEntryPath { get; set; } = string.Empty;
}
