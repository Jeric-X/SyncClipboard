using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.Runtime)]
public record class LinuxRuntimeConfig
{
    public const string ConfigKey = "LinuxRuntime";

    public string AppImageEntryPath { get; set; } = string.Empty;
}
