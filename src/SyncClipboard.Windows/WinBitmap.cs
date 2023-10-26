using SyncClipboard.Abstract;
using System.Drawing;

namespace SyncClipboard.Windows;

public class WinBitmap : IClipboardImage
{
    private readonly Image _image;

    public WinBitmap(Image image)
    {
        ArgumentNullException.ThrowIfNull(image, nameof(image));
        _image = image;
    }

    public static WinBitmap? FromImage(Image? image)
    {
        return image is null ? null : new WinBitmap(image);
    }

    public void Save(string path)
    {
        _image.Save(path);
    }
}
