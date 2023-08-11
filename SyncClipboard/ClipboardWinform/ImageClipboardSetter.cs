using ImageMagick;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System.IO;
using System.Windows.Forms;

namespace SyncClipboard.ClipboardWinform;

internal class ImageClipboardSetter : FileClipboardSetter, IClipboardSetter<Core.Clipboard.ImageProfile>
{
    public override object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation)
    {
        var obj = base.CreateClipboardObjectContainer(metaInfomation);
        var dataObject = obj as DataObject;

        SetHtml(dataObject, metaInfomation.Files[0]);
        SetQqFormat(dataObject, metaInfomation.Files[0]);
        SetBitmap(dataObject, metaInfomation.Files[0]);

        return dataObject;
    }

    private static void SetBitmap(DataObject dataObject, string imagePath)
    {
        using var image = new MagickImage(imagePath);
        dataObject.SetData(DataFormats.Bitmap, image.ToBitmap());
    }

    private static void SetHtml(DataObject dataObject, string imagePath)
    {
        string clipboardHtml = ClipboardImageBuilder.GetClipboardHtml(imagePath);
        dataObject.SetData(DataFormats.Html, clipboardHtml);
    }

    private static void SetQqFormat(DataObject dataObject, string imagePath)
    {
        string clipboardQq = ClipboardImageBuilder.GetClipboardQQFormat(imagePath);
        MemoryStream ms = new(System.Text.Encoding.UTF8.GetBytes(clipboardQq));
        dataObject.SetData("QQ_Unicode_RichEdit_Format", ms);
    }
}
