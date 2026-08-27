using SyncClipboard.Shared.Attributes;
using System.Text.Json.Serialization;

namespace SyncClipboard.Core.Models.UserConfigs;

[ConfigKey(ConfigKey, ConfigStorage.UpdateInfo)]
public record class UpdateInfoConfig
{
    public const string ConfigKey = "UpdateInfo";

    public const string TypeExternal = "external";
    public const string TypeMarket = "market";
    public const string TypeManual = "manual";

    [JsonPropertyName("manage_type")]
    public string ManageType { get; set; } = string.Empty;
    [JsonPropertyName("update_src")]
    public string UpdateSrc { get; set; } = string.Empty;
    [JsonPropertyName("package_name")]
    public string PackageName { get; set; } = string.Empty;
}
