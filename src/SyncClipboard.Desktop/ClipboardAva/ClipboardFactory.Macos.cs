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
    private List<HandlerMapping> MacFormatHandlerlist =>
    [
        new HandlerMapping(Format.FileList, HandleFiles),
        new HandlerMapping(Format.PublicTiff, HandleMacosImage),
        new HandlerMapping(Format.PublicHtml, HandleMacHtml),
        new HandlerMapping(Format.MacText, HandleMacText),
        new HandlerMapping(Format.NSpasteboardConcealed, HandleTransient),
        new HandlerMapping(Format.NSPasteboardTransient, HandleTransient),
    ];

    [SupportedOSPlatform("macos")]
    private async Task<ClipboardMetaInfomation> HandleMacosClipboard(string[] formats, CancellationToken token)
    {
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
    private static readonly string[] MacImageTypeList =
    [
        Format.PublicTiff,
        Format.PublicPng,
    ];

    [SupportedOSPlatform("macos")]
    private async Task HandleMacosImage(ClipboardMetaInfomation meta, CancellationToken token)
    {
        meta.OriginalType = ClipboardMetaInfomation.ImageType;

        foreach (var imagetype in MacImageTypeList)
        {
            var bytes = await Clipboard.GetDataAsync(imagetype, token) as byte[];
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
        var htmlBytes = await Clipboard.GetDataAsync(Format.PublicHtml, token) as byte[];
        ArgumentNullException.ThrowIfNull(htmlBytes);
        meta.Html = Encoding.UTF8.GetString(htmlBytes);
    }

    [SupportedOSPlatform("macos")]
    private async Task HandleMacText(ClipboardMetaInfomation meta, CancellationToken token)
    {
        meta.Text = await Clipboard.GetTextAsync(token);
    }

    [SupportedOSPlatform("macos")]
    private Task HandleTransient(ClipboardMetaInfomation meta, CancellationToken _)
    {
        meta.ExcludeForHistory = true;
        meta.ExcludeForSync = true;
        return Task.CompletedTask;
    }
}