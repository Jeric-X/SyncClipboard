using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public class ImageProfile : FileProfile
{
    public override ProfileType Type => ProfileType.Image;

    public ImageProfile(string fullPath, string? fileName = null, string? hash = null)
        : base(fullPath, fileName, hash)
    {
    }

    public ImageProfile(HistoryRecord record) : base(record)
    {
    }

    public ImageProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    public static string CreateNewDataFileName()
    {
        return Path.Combine(Env.ImageTemplateFolder, $"Image_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Path.GetRandomFileName()}.{GetImageExtention()}");
    }

    private static string GetImageExtention()
    {
        return "png";
    }
}