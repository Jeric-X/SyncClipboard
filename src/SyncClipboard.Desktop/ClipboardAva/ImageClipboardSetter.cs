using Avalonia.Input;
using ImageMagick;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class ImageClipboardSetter : FileClipboardSetter, IClipboardSetter<Abstract.Profiles.ImageProfile>
{
    protected override DataObject CreatePackage(ClipboardMetaInfomation metaInfomation)
    {
        var dataObject = base.CreatePackage(metaInfomation);
        string clipboardHtml = ClipboardImageBuilder.GetClipboardHtml(metaInfomation.Files![0]);

        if (OperatingSystem.IsLinux())
        {
            SetImage(dataObject, metaInfomation.Files![0], LinuxImageFormat);
            dataObject.Set(Format.TextHtml, System.Text.Encoding.UTF8.GetBytes(clipboardHtml));
        }
        else if (OperatingSystem.IsMacOS())
        {
            SetImage(dataObject, metaInfomation.Files![0], MacImageFormat);
            dataObject.Set(Format.PublicHtml, System.Text.Encoding.UTF8.GetBytes(clipboardHtml));
        }

        string clipboardQq = ClipboardImageBuilder.GetClipboardQQFormat(metaInfomation.Files![0]);
        dataObject.Set("QQ_Unicode_RichEdit_Format", System.Text.Encoding.UTF8.GetBytes(clipboardQq));

        return dataObject;
    }

    [SupportedOSPlatform("linux")]
    private static readonly Dictionary<string, MagickFormat> LinuxImageFormat = new Dictionary<string, MagickFormat>
    {
        [Format.ImagePng] = MagickFormat.Png,
        [Format.ImageJpeg] = MagickFormat.Jpeg,
        [Format.ImageBmp] = MagickFormat.Bmp,
    };

    [SupportedOSPlatform("macos")]
    private static readonly Dictionary<string, MagickFormat> MacImageFormat = new Dictionary<string, MagickFormat>
    {
        [Format.PublicPng] = MagickFormat.Png,
        [Format.PublicTiff] = MagickFormat.Tiff,
    };

    private static void SetImage(DataObject dataObject, string path, Dictionary<string, MagickFormat> mapper)
    {
        using var magickImage = new MagickImage(path);

        foreach (var imageType in mapper)
        {
            using var stream = new MemoryStream();
            magickImage.Write(stream, imageType.Value);
            dataObject.Set(imageType.Key, stream.ToArray());
        }
    }
}
