using Avalonia.Input.Platform;
using FluentAvalonia.Core;
using SyncClipboard.Core.Models;
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal partial class ClipboardFactory
{
    [SupportedOSPlatform("linux")]
    private static async Task<ClipboardMetaInfomation> HandleLinuxClipboard(CancellationToken token)
    {
        var clipboard = App.Current.MainWindow.Clipboard!;
        var formats = await clipboard.GetFormatsAsync().WaitAsync(token);
        if (formats.Contains(Format.UriList))
        {
            return await HandleLinuxFile(clipboard, token);
        }

        //if (formats.Contains(Format.ImagePng))
        //{
        //    return await HandleLinuxImage(clipboard, token);
        //}

        var text = await clipboard?.GetTextAsync().WaitAsync(token)!;
        if (text?.StartsWith('\ufffd') ?? false)
        {
            throw new Exception($"wrong clipboard with: \\ufffd");
        }

        return new ClipboardMetaInfomation
        {
            Text = text
        };
    }

    [SupportedOSPlatform("linux")]
    private static async Task<ClipboardMetaInfomation> HandleLinuxFile(IClipboard clipboard, CancellationToken token)
    {
        var uriListbytes = await clipboard.GetDataAsync(Format.UriList).WaitAsync(token) as byte[];
        ArgumentNullException.ThrowIfNull(uriListbytes);
        var uriList = Encoding.UTF8.GetString(uriListbytes!);
        return new ClipboardMetaInfomation
        {
            Files = uriList
                .Split('\n')
                .Select(x => x.Replace("\r", ""))
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => new Uri(x).LocalPath).ToArray()
        };
    }

    private static readonly string[] ImageTypeList = new[]
    {
        Format.ImagePng,
        "image/jpeg",
        "image/bmp",
    };

    [SupportedOSPlatform("linux")]
    private static async Task<ClipboardMetaInfomation> HandleLinuxImage(IClipboard clipboard, CancellationToken token)
    {
        foreach (var imagetype in ImageTypeList)
        {
            try
            {
                var bytes = await clipboard.GetDataAsync(Format.ImagePng).WaitAsync(token) as byte[];
            }
            catch { }
        }
        return new();
    }
}
