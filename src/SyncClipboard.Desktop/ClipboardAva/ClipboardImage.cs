using ImageMagick;
using SyncClipboard.Abstract;
using System;

namespace SyncClipboard.Desktop.ClipboardAva;

public class ClipboardImage : IClipboardImage
{
    private readonly byte[] _imageBytes;

    public ClipboardImage(byte[]? imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes, nameof(imageBytes));
        _imageBytes = imageBytes;
    }

    public void Save(string path)
    {
        using MagickImage magickImage = new(_imageBytes);
        magickImage.Write(path);
    }
}