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
        if (metaInfomation.Files is null || metaInfomation.Files.Length == 0)
        {
            throw new ArgumentException("Not Contain Image.");
        }

        if (package is not DataTransfer dataTransfer)
        {
            return;
        }

        string imagePath = metaInfomation.Files[0];
        var item = new DataTransferItem();

        await FillFileItem(item, imagePath);

        // 添加图片特定格式
        FillItemImageFormats(item, imagePath);

        dataTransfer.Add(item);
    }

    private void FillItemImageFormats(DataTransferItem item, string imagePath)
    {
        try
        {
            // 不能dispose bitmap，图片仍关联着程序
            var bitmap = new Bitmap(imagePath);
            item.Set(DataFormat.Bitmap, bitmap);
        }
        catch (Exception ex)
        {
            _ = _logger.WriteAsync(LOG_TAG, $"Failed to load image: {imagePath}, {ex.Message}");
        }

        string clipboardHtml = ClipboardImageBuilder.GetClipboardHtml(imagePath);

        if (OperatingSystem.IsLinux())
        {
            FillLinuxFileItem(item, [imagePath]);
            SetPlatformImageFormats(item, imagePath, LinuxImageFormat);
            item.Set(DataFormat.CreateBytesPlatformFormat(Format.TextHtml), System.Text.Encoding.UTF8.GetBytes(clipboardHtml));
        }
        else if (OperatingSystem.IsMacOS())
        {
            // SetPlatformImageFormats(item, imagePath, MacImageFormat); 完全靠avalonia统一的bitmap设置，稳定的话删除这条专用于macos的调用
            item.Set(DataFormat.CreateBytesPlatformFormat(Format.PublicHtml), System.Text.Encoding.UTF8.GetBytes(clipboardHtml));
        }
        else if (OperatingSystem.IsWindows())
        {
            item.Set(DataFormat.CreateBytesPlatformFormat("HTML Format"), System.Text.Encoding.UTF8.GetBytes(clipboardHtml));
        }

        string clipboardQq = ClipboardImageBuilder.GetClipboardQQFormat(imagePath);
        item.Set(DataFormat.CreateBytesPlatformFormat("QQ_Unicode_RichEdit_Format"), System.Text.Encoding.UTF8.GetBytes(clipboardQq));
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
