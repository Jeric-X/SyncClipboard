using SyncClipboard.Shared.Attributes;
using SyncClipboard.Shared.Interfaces;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record PerformanceConfig : IConfigValidator
{
    public const string ConfigKey = "Performance";

    public uint HistoryImageCacheSizeMB { get; set; } = 100;
    public uint HistoryImageCacheIdleTimeoutMinutes { get; set; } = 60;
    public int HistoryImageLoadConcurrency { get; set; } = 1;

    public void Validate() => ArgumentOutOfRangeException.ThrowIfLessThan(HistoryImageLoadConcurrency, 1);
}
