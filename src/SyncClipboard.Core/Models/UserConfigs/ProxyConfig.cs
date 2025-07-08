namespace SyncClipboard.Core.Models.UserConfigs;

public record class ProxyConfig
{
    public ProxyType Type { get; set; } = ProxyType.System;
    public string Address { get; set; } = string.Empty;
}
