using SyncClipboard.Abstract;
using SyncClipboard.Abstract.Models;

namespace SyncClipboard.Abstract.Profiles;

public class ImageProfile : FileProfile
{
    public override ProfileType Type => ProfileType.Image;

    public ImageProfile(string fullPath, string? fileName = null, string? hash = null)
        : base(fullPath, fileName, hash)
    {
    }

    public ImageProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    private static string GetImageExtention()
    {
        return "png";
    }
}