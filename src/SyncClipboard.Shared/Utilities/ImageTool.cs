namespace SyncClipboard.Shared.Utilities;

public static class ImageTool
{
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".gif", ".bmp", ".png"];

    public static bool FileIsImage(string filename)
    {
        string extension = Path.GetExtension(filename);
        foreach (var imageExtension in ImageExtensions)
        {
            if (imageExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}