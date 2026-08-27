using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record class ProxyConfig
{
    public const string ConfigKey = "Proxy";

    public ProxyType Type { get; set; } = ProxyType.System;
    public string Address { get; set; } = string.Empty;
}
