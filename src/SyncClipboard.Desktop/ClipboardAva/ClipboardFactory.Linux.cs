using Avalonia.Input.Platform;
using FluentAvalonia.Core;
using SyncClipboard.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HandlerMapping = System.Collections.Generic.KeyValuePair<
    string,
    System.Func<
        Avalonia.Input.Platform.IClipboard,
        System.Threading.CancellationToken,
        System.Threading.Tasks.Task<SyncClipboard.Core.Models.ClipboardMetaInfomation>
    >
>;

namespace SyncClipboard.Desktop.ClipboardAva;

internal partial class ClipboardFactory
{

    [SupportedOSPlatform("linux")]
    private static readonly List<HandlerMapping> FormatHandlerlist = new List<HandlerMapping>
    {
        new HandlerMapping(Format.UriList, HandleLinuxFile),
        new HandlerMapping(Format.GnomeFiles, HandleGnomeFile),
        new HandlerMapping(Format.ImagePng, HandleLinuxImage),
        new HandlerMapping(Format.Text, HandleLinuxText),
    };

    [SupportedOSPlatform("linux")]
    private static async Task<ClipboardMetaInfomation> HandleLinuxClipboard(CancellationToken token)
    {
        var clipboard = App.Current.MainWindow.Clipboard!;
        var formats = await clipboard.GetFormatsAsync().WaitAsync(token);

        foreach (var handlerMapping in FormatHandlerlist)
        {
            if (formats.Contains(handlerMapping.Key))
            {
                return await handlerMapping.Value.Invoke(clipboard, token);
            }
        }

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
        var uriList = Encoding.UTF8.GetString(uriListbytes);
        return new ClipboardMetaInfomation
        {
            Files = uriList
                .Split('\n')
                .Select(x => x.Replace("\r", ""))
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => new Uri(x).LocalPath).ToArray()
        };
    }

    [SupportedOSPlatform("linux")]
    private static async Task<ClipboardMetaInfomation> HandleLinuxText(IClipboard clipboard, CancellationToken token)
    {
        var bytes = await clipboard.GetDataAsync(Format.Text).WaitAsync(token) as byte[];
        ArgumentNullException.ThrowIfNull(bytes);
        return new ClipboardMetaInfomation
        {
            Text = Encoding.UTF8.GetString(bytes)
        };
    }

    [SupportedOSPlatform("linux")]
    private static readonly string[] ImageTypeList = new[]
    {
        Format.ImagePng,
        Format.ImageJpeg,
        Format.ImageBmp,
    };

    [SupportedOSPlatform("linux")]
    private static async Task<ClipboardMetaInfomation> HandleLinuxImage(IClipboard clipboard, CancellationToken token)
    {
        var meta = new ClipboardMetaInfomation();
        foreach (var imagetype in ImageTypeList)
        {
            var bytes = await clipboard.GetDataAsync(imagetype).WaitAsync(token) as byte[];
            if (bytes is not null)
            {
                meta.Image = new ClipboardImage(bytes);
                break;
            }
        }

        if (meta.Image is null)
        {
            string text = "Unknow Image";
            var timeStamp = await clipboard.GetDataAsync(Format.TimeStamp).WaitAsync(token) as byte[];
            if (timeStamp is not null)
            {
                text += BitConverter.ToInt32(timeStamp);
            }
            meta.Text = text;
        }

        var html = await clipboard.GetDataAsync("text/html").WaitAsync(token) as byte[];
        if (html is not null)
        {
            meta.Html = Encoding.UTF8.GetString(html);
        }

        return meta;
    }

    [SupportedOSPlatform("linux")]
    private static async Task<ClipboardMetaInfomation> HandleGnomeFile(IClipboard clipboard, CancellationToken token)
    {
        var bytes = await clipboard.GetDataAsync(Format.GnomeFiles).WaitAsync(token) as byte[];
        var str = Encoding.UTF8.GetString(bytes!);
        var pathList = str.Split('\n')
                            .Select(x => x.Replace("\r", ""))
                            .Where(x => !string.IsNullOrEmpty(x)).ToArray();
        var meta = new ClipboardMetaInfomation();
        if (pathList[0] == "cut")
        {
            meta.Effects = DragDropEffects.Move;
        }
        meta.Files = new[] { new Uri(pathList[1]).LocalPath };
        return meta;
    }
}