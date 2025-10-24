using System.Text.Json.Serialization;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared;

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

    public static Profile CreateProfile(ClipboardProfileDTO profileDTO)
    {
        switch (profileDTO.Type)
        {
            case ProfileType.Text:
                return new TextProfile(profileDTO.Clipboard);
            case ProfileType.File:
                {
                    if (ImageTool.FileIsImage(profileDTO.File))
                    {
                        return new ImageProfile(profileDTO);
                    }
                    return new FileProfile(profileDTO);
                }
            case ProfileType.Image:
                return new ImageProfile(profileDTO);
            case ProfileType.Group:
                return new GroupProfile(profileDTO);
        }

        return new UnknownProfile();
    }
}
