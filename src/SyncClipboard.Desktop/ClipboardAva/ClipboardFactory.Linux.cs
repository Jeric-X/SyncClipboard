using FluentAvalonia.Core;
using ImageMagick;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
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
    private ClipboardMetaInfomation _metaCache = new();

    [SupportedOSPlatform("linux")]
    private List<HandlerMapping> FormatHandlerlist =>
    [
        new HandlerMapping(Format.Text, (meta, token) => HandleLinuxText(Format.Text, meta, token)),
        new HandlerMapping(Format.Utf8String, (meta, token) => HandleLinuxText(Format.Utf8String, meta, token)),
        new HandlerMapping(Format.TextUtf8, (meta, token) => HandleLinuxText(Format.TextUtf8, meta, token)),
        new HandlerMapping(Format.TEXT, (meta, token) => HandleLinuxText(Format.TEXT, meta, token)),

        new HandlerMapping(Format.FileList, HandleFiles),
        new HandlerMapping(Format.UriList, HandleLinuxUriList),
        new HandlerMapping(Format.GnomeFiles, HandleGnomeFile),

        new HandlerMapping(Format.TextHtml, HandleLinuxHtml),
        new HandlerMapping(Format.CompoundText, HandleCompoundText),
        new HandlerMapping(Format.KdeCutSelection, HandleKdeCutSelection),
    ];

    [SupportedOSPlatform("linux")]
    private async Task<ClipboardMetaInfomation> HandleLinuxClipboard(string[] formats, CancellationToken token)
    {
        ClipboardMetaInfomation meta = new();
        bool hasExcoption = false;

        await HandleTimeStamp(formats, meta, token);
        if (meta.TimeStamp is not null && _metaCache.TimeStamp == meta.TimeStamp)
        {
            return _metaCache;
        }

        foreach (var handlerMapping in FormatHandlerlist)
        {
            if (formats.Contains(handlerMapping.Key))
            {
                try
                {
                    await handlerMapping.Value.Invoke(meta, token);
                }
                catch (Exception ex) when (token.IsCancellationRequested is false)
                {
                    await Logger.WriteAsync(ex.Message);
                    hasExcoption = true;
                }
            }
        }

        await HandleLinuxImage(meta, formats, token);

        if (hasExcoption && meta.Empty())
        {
            throw new Exception("Clipboard is empty because of exception");
        }

        _metaCache = meta;
        return meta;
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleTimeStamp(string[] formats, ClipboardMetaInfomation meta, CancellationToken token)
    {
        if (formats.Contains(Format.TimeStamp) is false)
        {
            return;
        }

        try
        {
            meta.TimeStamp = await Clipboard.GetTimeStamp(token);
        }
        catch (Exception ex) when (token.IsCancellationRequested is false)
        {
            await Logger.WriteAsync(ex.Message);
        }
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleLinuxUriList(ClipboardMetaInfomation meta, CancellationToken token)
    {
        if (meta.Files is not null) return;

        var uriListbytes = await Clipboard.GetDataAsync(Format.UriList, token) as byte[];
        ArgumentNullException.ThrowIfNull(uriListbytes, nameof(HandleLinuxUriList));
        var uriListStr = Encoding.UTF8.GetString(uriListbytes);
        meta.Files = GetValidPathFromList(uriListStr.Split(["\r\n", "\r", "\n"], StringSplitOptions.None));
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleLinuxText(string format, ClipboardMetaInfomation meta, CancellationToken token)
    {
        if (meta.Text is not null)
        {
            return;
        }

        var data = await Clipboard.GetDataAsync(format, token);
        if (data is string text)
        {
            meta.Text = text;
        }
        else if (data is byte[] textBytes)
        {
            meta.Text = Encoding.UTF8.GetString(textBytes);
        }
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleLinuxHtml(ClipboardMetaInfomation meta, CancellationToken token)
    {
        var htmlBytes = await Clipboard.GetDataAsync(Format.TextHtml, token) as byte[];
        ArgumentNullException.ThrowIfNull(htmlBytes, nameof(HandleLinuxHtml));
        meta.Html = Encoding.UTF8.GetString(htmlBytes);
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleAllImageTypeData(ClipboardMetaInfomation meta, string[] formats, CancellationToken token)
    {
        foreach (var type in formats)
        {
            if (type.StartsWith("image/") is false)
            {
                continue;
            }

            meta.OriginalType = ClipboardMetaInfomation.ImageType;
            try
            {
                if (await Clipboard.GetDataAsync(type, token) is not byte[] bytes)
                {
                    continue;
                }

                meta.Image = ClipboardImage.TryCreateImage(bytes);
                if (meta.Image != null)
                {
                    break;
                }
            }
            catch (Exception ex) when (token.IsCancellationRequested is false)
            {
                await Logger.WriteAsync(ex.Message);
            }
        }
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleLinuxImage(ClipboardMetaInfomation meta, string[] formats, CancellationToken token)
    {
        await HandleAllImageTypeData(meta, formats, token);

        if (meta.OriginalType == ClipboardMetaInfomation.ImageType && meta.Image is null && meta.Files is null)
        {
            if (Path.Exists(meta.Text))
            {
                meta.Files = [meta.Text];
            }
            else
            {
                throw new Exception("Can't get image from clipboard");
            }
        }
    }

    [SupportedOSPlatform("linux")]
    private string[] GetValidPathFromList(IEnumerable<string> pathList)
    {
        var erroCount = 0;
        List<string> uriList = [];
        foreach (var line in pathList)
        {
            try
            {
                uriList.Add(new Uri(line).LocalPath);
            }
            catch
            {
                erroCount++;
                if (erroCount >= 8)
                {
                    Logger.Write($"can't get files from path list");
                    return uriList.ToArray();
                }
            }
        }
        return uriList.ToArray();
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleGnomeFile(ClipboardMetaInfomation meta, CancellationToken token)
    {
        if (meta.Files is not null) return;

        var bytes = await Clipboard.GetDataAsync(Format.GnomeFiles, token) as byte[];
        ArgumentNullException.ThrowIfNull(bytes, nameof(HandleGnomeFile));
        var str = Encoding.UTF8.GetString(bytes!);
        var pathList = str.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)
                            .Where(x => !string.IsNullOrEmpty(x)).ToArray();
        if (pathList.Length < 2) return;
        if (pathList[0] == "cut")
        {
            meta.Effects = DragDropEffects.Move;
        }

        meta.Files = GetValidPathFromList(pathList);
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleCompoundText(ClipboardMetaInfomation meta, CancellationToken token)
    {
        var bytes = await Clipboard.GetDataAsync(Format.CompoundText, token) as byte[];
        ArgumentNullException.ThrowIfNull(bytes, nameof(HandleCompoundText));
        var str = Encoding.UTF8.GetString(bytes!);
        string[] lines = str.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        if (lines.Length >= 3 && lines[1] == "cut")
        {
            meta.Effects = DragDropEffects.Move;
        }
        meta.Text ??= str;
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleKdeCutSelection(ClipboardMetaInfomation meta, CancellationToken token)
    {
        var bytes = await Clipboard.GetDataAsync(Format.KdeCutSelection, token) as byte[];
        ArgumentNullException.ThrowIfNull(bytes, nameof(HandleKdeCutSelection));
        var str = Encoding.UTF8.GetString(bytes!);
        if (str == "1")
        {
            meta.Effects = DragDropEffects.Move;
        }
    }
}
