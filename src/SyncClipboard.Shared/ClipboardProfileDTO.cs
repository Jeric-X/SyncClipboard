using System.Text.Json.Serialization;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared;

[Obsolete("Use ProfileDto instead")]
public record class ClipboardProfileDTO
{
    [JsonPropertyName(nameof(File))]
    public string File { get; set; }
    [JsonPropertyName(nameof(Clipboard))]
    public string Clipboard { get; set; }
    [JsonPropertyName(nameof(Type))]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProfileType Type { get; set; }

    public ClipboardProfileDTO(string file = "", string clipboard = "", ProfileType type = ProfileType.Text)
    {
        File = file;
        Clipboard = clipboard;
        Type = type;
    }

    [Obsolete("Use Profile.Create(ProfileDto) instead")]
    public static Profile CreateProfile(ClipboardProfileDTO profileDTO, bool ignoreHash = false)
    {
        throw new NotSupportedException("ClipboardProfileDTO is obsolete. Use Profile.Create(ProfileDto) instead.");
    }
}
