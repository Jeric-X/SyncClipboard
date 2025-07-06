using System.Text.Json.Serialization;

namespace SyncClipboard.Core.Utilities.Updater;

public class GitHubRelease
{
    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("size")]
        public ulong Size { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }
    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }
    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}
