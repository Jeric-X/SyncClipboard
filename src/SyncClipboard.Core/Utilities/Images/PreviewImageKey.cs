namespace SyncClipboard.Core.Utilities.Images;

/// <summary>File version and decoding parameters; cached header hints are deliberately excluded.</summary>
public readonly record struct PreviewImageKey(
    string Path, long Length, long LastWriteTicks, double Width, double Height,
    double Scale, int MaxPixelWidth, bool SizeOnly, bool IsThumbnail = false)
{
    public static PreviewImageKey Create(string path, double width, double height, double scale,
        int maxPixelWidth, bool sizeOnly, bool isThumbnail = false)
    {
        var file = new FileInfo(path);
        return new(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks,
            width, height, scale, maxPixelWidth, sizeOnly, isThumbnail);
    }

    public bool IsOlderVersionOf(PreviewImageKey other) =>
        Path == other.Path && (Length != other.Length || LastWriteTicks != other.LastWriteTicks);
}
