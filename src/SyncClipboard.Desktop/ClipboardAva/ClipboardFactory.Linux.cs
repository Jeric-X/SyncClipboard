using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
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
    private async Task<ClipboardMetaInfomation> HandleLinuxClipboard(string[] formats, bool hasFingerprint, int? fingerprint, CancellationToken token)
    {
        ClipboardMetaInfomation meta = new();
        if (hasFingerprint)
        {
            meta.TimeStamp = fingerprint;
        }
        else
        {
            await HandleTimeStamp(formats, meta, token);
        }

        bool hasExcoption = false;
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
            // On Wayland, retrieving the timestamp from a native Wayland clipboard owner can time out.
            // https://github.com/Jeric-X/SyncClipboard/issues/391
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(100));

            meta.TimeStamp = await Clipboard.GetTimeStamp(timeout.Token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested is false)
        {
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

        var uriListStr = await Clipboard.GetStringAsync(Format.UriList, token);
        ArgumentNullException.ThrowIfNull(uriListStr, nameof(HandleLinuxUriList));
        meta.Files = GetValidPathFromList(uriListStr.Split(["\r\n", "\r", "\n"], StringSplitOptions.None));
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleLinuxText(string format, ClipboardMetaInfomation meta, CancellationToken token)
    {
        if (meta.Text is not null)
        {
            return;
        }

        // Avalonia's universal Text format is distinct from a platform format with the same identifier.
        meta.Text = format == Format.Text
            ? await Clipboard.GetTextAsync(token)
            : await Clipboard.GetStringAsync(format, token);
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleLinuxHtml(ClipboardMetaInfomation meta, CancellationToken token)
    {
        var html = await Clipboard.GetStringAsync(Format.TextHtml, token);
        ArgumentNullException.ThrowIfNull(html, nameof(HandleLinuxHtml));
        meta.Html = html;
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
        await HandleBitmap(meta, token);

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

        var str = await Clipboard.GetStringAsync(Format.GnomeFiles, token);
        ArgumentNullException.ThrowIfNull(str, nameof(HandleGnomeFile));
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
        var str = await Clipboard.GetStringAsync(Format.CompoundText, token);
        ArgumentNullException.ThrowIfNull(str, nameof(HandleCompoundText));
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
        var str = await Clipboard.GetStringAsync(Format.KdeCutSelection, token);
        ArgumentNullException.ThrowIfNull(str, nameof(HandleKdeCutSelection));
        if (str == "1")
        {
            meta.Effects = DragDropEffects.Move;
        }
    }
}
