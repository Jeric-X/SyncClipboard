using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Profiles;

public class ImageProfile : FileProfile
{
    public override ProfileType Type => ProfileType.Image;

    public ImageProfile(ProfilePersistentInfo entity) : base(entity)
    {
    }

    public ImageProfile(string? fullPath, string? fileName = null, string? hash = null)
        : base(fullPath, fileName, hash)
    {
    }

    public ImageProfile(ProfileDto dto) : base(dto)
    {
    }

    public static string CreateImageFileName()
    {
        return $"Image_{Utility.CreateTimeBasedFileName()}.png";
    }
}