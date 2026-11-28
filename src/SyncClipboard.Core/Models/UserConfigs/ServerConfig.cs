using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record class ServerConfig
{
    public const string ConfigKey = "ServerService";

    public bool SwitchOn { get; set; } = false;
    public ushort Port { get; set; } = 5033;
    public bool EnableHttps { get; set; } = false;
    public string CertificatePemPath { get; set; } = string.Empty;
    public string CertificatePemKeyPath { get; set; } = string.Empty;
    public bool EnableCustomConfigurationFile { get; set; } = false;
    public string CustomConfigurationFilePath { get; set; } = string.Empty;
}
