using SyncClipboard.Shared.Attributes;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.Static)]
public record class EnvConfig
{
    public const string ConfigKey = "Env";

    public bool PortableUserConfig { get; set; } = false;

    public bool PortableAppDataFolder { get; set; } = false;
}
