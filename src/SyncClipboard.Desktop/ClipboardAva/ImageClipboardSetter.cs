using Avalonia.Input;
using ImageMagick;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class ImageClipboardSetter : FileClipboardSetter, IClipboardSetter<Core.Clipboard.ImageProfile>
{
    public override object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation)
    {
        var dataObject = (DataObject)base.CreateClipboardObjectContainer(metaInfomation);
        if (OperatingSystem.IsLinux())
        {
            SetLinux(dataObject, metaInfomation.Files![0]);
        }

        string clipboardQq = ClipboardImageBuilder.GetClipboardQQFormat(metaInfomation.Files![0]);
        dataObject.Set("QQ_Unicode_RichEdit_Format", System.Text.Encoding.UTF8.GetBytes(clipboardQq));

        return dataObject;
    }

    private static readonly Dictionary<string, MagickFormat> FormatHandlerlist = new Dictionary<string, MagickFormat>
    {
        [Format.ImagePng] = MagickFormat.Png,
        [Format.ImageJpeg] = MagickFormat.Jpeg,
        [Format.ImageBmp] = MagickFormat.Bmp,
    };

    [SupportedOSPlatform("linux")]
    private static void SetLinux(DataObject dataObject, string path)
    {
        using var magickImage = new MagickImage(path);

        foreach (var imageType in FormatHandlerlist)
        {
            using var stream = new MemoryStream();
            magickImage.Write(stream, imageType.Value);
            dataObject.Set(imageType.Key, stream.ToArray());
        }
    }
}
