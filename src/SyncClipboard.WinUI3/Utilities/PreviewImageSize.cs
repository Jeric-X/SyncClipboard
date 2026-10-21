using System;
using System.IO;

namespace SyncClipboard.WinUI3.Utilities;

internal readonly record struct PreviewImageSize(double Width, double Height, int PixelWidth, int PixelHeight)
{
    public static PreviewImageSize Calculate(uint width, uint height, bool thumbnail, double scale)
    {
        if (width == 0 || height == 0 || !double.IsFinite(scale) || scale <= 0)
        {
            throw new InvalidDataException("Invalid image dimensions or display scale.");
        }

        var maxWidth = thumbnail ? 5120 : 2048;
        var maxHeight = thumbnail ? 200 : 2048;
        var displayRatio = Math.Min(1, Math.Min((double)maxWidth / width, (double)maxHeight / height));
        var pixelRatio = Math.Min(1, Math.Min(displayRatio * (thumbnail ? scale : 1), (double)maxWidth / width));
        if (width * pixelRatio < 1 || height * pixelRatio < 1)
        {
            throw new InvalidDataException("Scaled image dimensions are smaller than one pixel.");
        }

        return new(width * displayRatio, height * displayRatio,
            (int)Math.Floor(width * pixelRatio), (int)Math.Floor(height * pixelRatio));
    }
}
