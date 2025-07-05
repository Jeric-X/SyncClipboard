using System.Text.Json.Serialization;

namespace SyncClipboard.Core.Models.UserConfigs;

public record class UpdateInfoConfig
{
    public const string TypeExternal = "external";
    public const string TypeManual = "manual";

    [JsonPropertyName("manage_type")]
    public string ManageType { get; set; } = string.Empty;
    [JsonPropertyName("update_src")]
    public string UpdateSrc { get; set; } = string.Empty;
    [JsonPropertyName("package_name")]
    public string PackageName { get; set; } = string.Empty;
}
