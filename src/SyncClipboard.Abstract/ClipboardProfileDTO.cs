using System.Text.Json.Serialization;

namespace SyncClipboard.Abstract;

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
}
