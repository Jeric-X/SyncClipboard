using Avalonia.Platform.Storage;
using FluentAvalonia.Core;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
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
        SyncClipboard.Core.Models.ClipboardMetaInfomation,
        System.Threading.CancellationToken,
        System.Threading.Tasks.Task
    >
>;

namespace SyncClipboard.Desktop.ClipboardAva;

internal partial class ClipboardFactory
{
    [SupportedOSPlatform("macos")]
    private List<HandlerMapping> MacFormatHandlerlist => new()
    {
        new HandlerMapping(Format.FileList, HandleMacosFile),
        new HandlerMapping(Format.PublicTiff, HandleMacosImage),
        new HandlerMapping(Format.PublicHtml, HandleMacHtml),
        new HandlerMapping(Format.MacText, HandleMacText),
    };

    [SupportedOSPlatform("macos")]
    private async Task<ClipboardMetaInfomation> HandleMacosClipboard(CancellationToken token)
    {
        var clipboard = App.Current.MainWindow.Clipboard!;
        var formats = await clipboard.GetFormatsAsync().WaitAsync(token);

        ClipboardMetaInfomation meta = new();
        foreach (var handlerMapping in MacFormatHandlerlist)
        {
            if (formats.Contains(handlerMapping.Key))
            {
                await handlerMapping.Value.Invoke(meta, token);
            }
        }

        return meta;
    }

    [SupportedOSPlatform("macos")]
    private async Task HandleMacosFile(ClipboardMetaInfomation meta, CancellationToken token)
    {
        var items = await Clipboard.GetDataAsync(Format.FileList).WaitAsync(token) as IEnumerable<IStorageItem>;
        meta.Files = items?.Select(item => item.Path.LocalPath).ToArray();
    }

    [SupportedOSPlatform("macos")]
    private static readonly string[] MacImageTypeList = new[]
    {
        Format.PublicTiff,
        Format.PublicPng,
    };

    [SupportedOSPlatform("macos")]
    private async Task HandleMacosImage(ClipboardMetaInfomation meta, CancellationToken token)
    {
        meta.OriginalType = ClipboardMetaInfomation.ImageType;

        foreach (var imagetype in MacImageTypeList)
        {
            var bytes = await Clipboard.GetDataAsync(imagetype).WaitAsync(token) as byte[];
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
    }

    [SupportedOSPlatform("macos")]
    private async Task HandleMacHtml(ClipboardMetaInfomation meta, CancellationToken token)
    {
        var htmlBytes = await Clipboard.GetDataAsync(Format.PublicHtml).WaitAsync(token) as byte[];
        ArgumentNullException.ThrowIfNull(htmlBytes);
        meta.Html = Encoding.UTF8.GetString(htmlBytes);
    }

    [SupportedOSPlatform("macos")]
    private async Task HandleMacText(ClipboardMetaInfomation meta, CancellationToken token)
    {
        meta.Text = await Clipboard?.GetTextAsync().WaitAsync(token)!;
    }
}