using Avalonia.Input;
using ImageMagick;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class ImageClipboardSetter : FileClipboardSetter, IClipboardSetter<SyncClipboard.Shared.Profiles.ImageProfile>
{
    public override async Task FillPackage(object package, ClipboardMetaInfomation metaInfomation)
    {
        await base.FillPackage(package, metaInfomation);

        if (package is not DataTransfer dataTransfer)
        {
            return;
        }

        string clipboardHtml = ClipboardImageBuilder.GetClipboardHtml(metaInfomation.Files![0]);

        var item = new DataTransferItem();

        if (OperatingSystem.IsLinux())
        {
            SetImage(item, metaInfomation.Files![0], LinuxImageFormat);
            item.Set(DataFormat.CreateBytesPlatformFormat(Format.TextHtml), System.Text.Encoding.UTF8.GetBytes(clipboardHtml));
        }
        else if (OperatingSystem.IsMacOS())
        {
            SetImage(item, metaInfomation.Files![0], MacImageFormat);
            item.Set(DataFormat.CreateBytesPlatformFormat(Format.PublicHtml), System.Text.Encoding.UTF8.GetBytes(clipboardHtml));
        }

        string clipboardQq = ClipboardImageBuilder.GetClipboardQQFormat(metaInfomation.Files![0]);
        item.Set(DataFormat.CreateBytesPlatformFormat("QQ_Unicode_RichEdit_Format"), System.Text.Encoding.UTF8.GetBytes(clipboardQq));

        dataTransfer.Add(item);
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

    private static void SetImage(DataTransferItem item, string path, Dictionary<string, MagickFormat> mapper)
    {
        using var magickImage = new MagickImage(path);

        foreach (var imageType in mapper)
        {
            using var stream = new MemoryStream();
            magickImage.Write(stream, imageType.Value);
            item.Set(DataFormat.CreateBytesPlatformFormat(imageType.Key), stream.ToArray());
        }
    }
}
