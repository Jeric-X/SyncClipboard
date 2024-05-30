using System.Text.Json.Serialization;

namespace SyncClipboard.Core.Utilities.Updater;

public class GitHubRelease
{
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }
    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }
}
