using System.Text.Json.Serialization;

namespace SyncClipboard.Abstract;

public record class ClipboardProfileDTO
{
    [JsonPropertyName(nameof(File))]
    public string File { get; set; }
    [JsonPropertyName(nameof(Clipboard))]
    public string Clipboard { get; set; }
    [JsonPropertyName(nameof(Type))]
    public string Type { get; set; }

    public ClipboardProfileDTO(string file, string clipboard, string type)
    {
        File = file;
        Clipboard = clipboard;
        Type = type;
    }
}
