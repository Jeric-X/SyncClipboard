using Avalonia.Input;
using Avalonia.Media.Imaging;
using ImageMagick;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class ImageClipboardSetter(ILogger logger) : FileClipboardSetter, IClipboardSetter<SyncClipboard.Shared.Profiles.ImageProfile>
{
    private readonly ILogger _logger = logger;
    private const string LOG_TAG = nameof(ImageClipboardSetter);

    public override async Task FillPackage(object package, ClipboardMetaInfomation metaInfomation)
    {
        await base.FillPackage(package, metaInfomation);

        if (package is not DataTransfer dataTransfer)
        {
            return;
        }

        string imagePath = metaInfomation.Files![0];
        string clipboardHtml = ClipboardImageBuilder.GetClipboardHtml(imagePath);

        var item = new DataTransferItem();

        // 不能dispose bitmap，图片仍关联着程序
        try
        {
            var bitmap = new Bitmap(imagePath);
            item.Set(DataFormat.Bitmap, bitmap);
        }
        catch (Exception ex)
        {
            await _logger.WriteAsync(LOG_TAG, $"Failed to load image: {imagePath}, {ex.Message}");
        }

        if (OperatingSystem.IsLinux())
        {
            SetPlatformImageFormats(item, imagePath, LinuxImageFormat);
            item.Set(DataFormat.CreateBytesPlatformFormat(Format.TextHtml), System.Text.Encoding.UTF8.GetBytes(clipboardHtml));
        }
        else if (OperatingSystem.IsMacOS())
        {
            SetPlatformImageFormats(item, imagePath, MacImageFormat);
            item.Set(DataFormat.CreateBytesPlatformFormat(Format.PublicHtml), System.Text.Encoding.UTF8.GetBytes(clipboardHtml));
        }
        else if (OperatingSystem.IsWindows())
        {
            item.Set(DataFormat.CreateBytesPlatformFormat("HTML Format"), System.Text.Encoding.UTF8.GetBytes(clipboardHtml));
        }

        string clipboardQq = ClipboardImageBuilder.GetClipboardQQFormat(imagePath);
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

    private static void SetPlatformImageFormats(DataTransferItem item, string path, Dictionary<string, MagickFormat> mapper)
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
