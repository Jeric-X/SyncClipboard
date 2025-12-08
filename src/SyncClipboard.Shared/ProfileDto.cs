using SyncClipboard.Shared.Profiles;
using System.Text.Json.Serialization;

namespace SyncClipboard.Shared;

public record class ProfileDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProfileType Type { get; set; } = ProfileType.Text;
    public string Hash { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool HasData { get; set; } = false;
    public string? DataName { get; set; }
    public long Size { get; set; } = 0;
}
