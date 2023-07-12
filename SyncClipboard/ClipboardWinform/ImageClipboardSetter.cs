using ImageMagick;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Utilities;
using System.IO;
using System.Windows.Forms;

namespace SyncClipboard.ClipboardWinform;

internal class ImageClipboardSetter : FileClipboardSetter, IClipboardSetter<Core.Clipboard.ImageProfile>
{
    public override object CreateClipboardObjectContainer(MetaInfomation metaInfomation)
    {
        var obj = base.CreateClipboardObjectContainer(metaInfomation);
        var dataObject = obj as DataObject;

        SetHtml(dataObject, metaInfomation.Files[0]);
        SetQqFormat(dataObject, metaInfomation.Files[0]);
        SetBitmap(dataObject, metaInfomation.Files[0]);

        dataObject.SetFileDropList(new System.Collections.Specialized.StringCollection { metaInfomation.Files[0] });
        return dataObject;
    }

    private static void SetBitmap(DataObject dataObject, string imagePath)
    {
        using var image = new MagickImage(imagePath);
        dataObject.SetData(DataFormats.Bitmap, image.ToBitmap());
    }

    private static void SetHtml(DataObject dataObject, string imagePath)
    {
        string html = $@"<img src=""file:///{imagePath}"">";
        string clipboardHtml = ClipboardHtmlBuilder.GetClipboardHtml(html);
        dataObject.SetData(DataFormats.Html, clipboardHtml);
    }

    private const string clipboardQqFormat = @"<QQRichEditFormat>
<Info version=""1001"">
</Info>
<EditElement type=""1"" imagebiztype=""0"" textsummary="""" filepath=""<<<<<<"" shortcut="""">
</EditElement>
</QQRichEditFormat>";

    private static void SetQqFormat(DataObject dataObject, string imagePath)
    {
        string clipboardQq = clipboardQqFormat.Replace("<<<<<<", imagePath);
        MemoryStream ms = new(System.Text.Encoding.UTF8.GetBytes(clipboardQq));
        dataObject.SetData("QQ_Unicode_RichEdit_Format", ms);
    }
}
