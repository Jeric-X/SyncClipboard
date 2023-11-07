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

    [SupportedOSPlatform("macos")]
    private static readonly List<HandlerMapping> MacFormatHandlerlist = new List<HandlerMapping>
    {
        new HandlerMapping(Format.FileList, HandleMacosFile),
        new HandlerMapping(Format.PublicTiff, HandleMacosImage),
    };

    [SupportedOSPlatform("macos")]
    private static async Task<ClipboardMetaInfomation> HandleMacosClipboard(CancellationToken token)
    {
        var clipboard = App.Current.MainWindow.Clipboard!;
        var formats = await clipboard.GetFormatsAsync().WaitAsync(token);

        foreach (var handlerMapping in MacFormatHandlerlist)
        {
            if (formats.Contains(handlerMapping.Key))
            {
                return await handlerMapping.Value.Invoke(clipboard, token);
            }
        }

        var text = await clipboard?.GetTextAsync().WaitAsync(token)!;

        return new ClipboardMetaInfomation
        {
            Text = text
        };
    }

    [SupportedOSPlatform("macos")]
    private static async Task<ClipboardMetaInfomation> HandleMacosFile(IClipboard clipboard, CancellationToken token)
    {
        var uriListbytes = await clipboard.GetDataAsync(Format.FileList).WaitAsync(token) as byte[];
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

    [SupportedOSPlatform("macos")]
    private static readonly string[] MacImageTypeList = new[]
    {
        Format.PublicTiff,
        Format.PublicPng,
    };

    [SupportedOSPlatform("macos")]
    private static async Task<ClipboardMetaInfomation> HandleMacosImage(IClipboard clipboard, CancellationToken token)
    {
        var meta = new ClipboardMetaInfomation
        {
            OriginalType = ClipboardMetaInfomation.ImageType
        };

        foreach (var imagetype in MacImageTypeList)
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
            meta.Text = "Unknow Image";
        }

        var html = await clipboard.GetDataAsync(Format.PublicHtml).WaitAsync(token) as byte[];
        if (html is not null)
        {
            meta.Html = Encoding.UTF8.GetString(html);
        }

        return meta;
    }
}