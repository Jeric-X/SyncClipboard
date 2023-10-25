using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal class ImageClipboardSetter : FileClipboardSetter, IClipboardSetter<ImageProfile>
{
    public override object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation)
    {
        if (metaInfomation.Files is null || metaInfomation.Files.Length == 0)
        {
            throw new ArgumentException("Not Contain File.");
        }

        var obj = base.CreateClipboardObjectContainer(metaInfomation);
        DataPackage dataObject = (DataPackage)obj;

        SetHtml(dataObject, metaInfomation.Files[0]);
        SetQqFormat(dataObject, metaInfomation.Files[0]);
        SetBitmap(dataObject, metaInfomation.Files[0]);

        return dataObject;
    }

    private static void SetBitmap(DataPackage dataObject, string imagePath)
    {
        var storageFile = StorageFile.GetFileFromPathAsync(imagePath).AsTask().Result;
        var randomAccessStreamReference = RandomAccessStreamReference.CreateFromFile(storageFile);
        dataObject.SetBitmap(randomAccessStreamReference);
    }

    private static void SetHtml(DataPackage dataObject, string imagePath)
    {
        string clipboardHtml = ClipboardImageBuilder.GetClipboardHtml(imagePath);
        dataObject.SetHtmlFormat(clipboardHtml);
    }

    private static void SetQqFormat(DataPackage dataObject, string imagePath)
    {
        string clipboardQq = ClipboardImageBuilder.GetClipboardQQFormat(imagePath);
        using MemoryStream ms = new(System.Text.Encoding.UTF8.GetBytes(clipboardQq));
        var randomAccessStreamReference = RandomAccessStreamReference.CreateFromStream(ms.AsRandomAccessStream());
        dataObject.SetData("QQ_Unicode_RichEdit_Format", randomAccessStreamReference);
    }
}
