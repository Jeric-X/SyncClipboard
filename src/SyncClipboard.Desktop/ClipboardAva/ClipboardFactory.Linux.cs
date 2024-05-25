using Avalonia.Input.Platform;
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

using EffectsHandlerMapping = System.Collections.Generic.KeyValuePair<
   string,
   System.Func<
       Avalonia.Input.Platform.IClipboard,
       System.Threading.CancellationToken,
       System.Threading.Tasks.Task<SyncClipboard.Core.Models.DragDropEffects?>
   >
>;
namespace SyncClipboard.Desktop.ClipboardAva;

internal partial class ClipboardFactory
{
    [SupportedOSPlatform("linux")]
    private List<HandlerMapping> FormatHandlerlist => new List<HandlerMapping>
    {
        new HandlerMapping(Format.UriList, HandleLinuxFile),
        new HandlerMapping(Format.GnomeFiles, HandleGnomeFile),
        new HandlerMapping(Format.ImagePng, HandleLinuxImage),
        new HandlerMapping(Format.Text, HandleLinuxText),
        new HandlerMapping(Format.TextHtml, HandleLinuxHtml),
    };

    [SupportedOSPlatform("linux")]
    private async Task<ClipboardMetaInfomation> HandleLinuxClipboard(CancellationToken token)
    {
        var clipboard = App.Current.MainWindow.Clipboard!;
        var formats = await clipboard.GetFormatsAsync().WaitAsync(token);

        ClipboardMetaInfomation meta = new();
        foreach (var handlerMapping in FormatHandlerlist)
        {
            if (formats.Contains(handlerMapping.Key))
            {
                await handlerMapping.Value.Invoke(meta, token);
                meta.Effects ??= await ParseEffects(clipboard, formats, token);
            }
        }

        return meta;
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleLinuxFile(ClipboardMetaInfomation meta, CancellationToken token)
    {
        if (meta.Files is not null) return;

        var uriListbytes = await Clipboard.GetDataAsync(Format.UriList).WaitAsync(token) as byte[];
        ArgumentNullException.ThrowIfNull(uriListbytes);
        var uriList = Encoding.UTF8.GetString(uriListbytes);
        meta.Files = uriList
                .Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x =>
                {
                    try { return new Uri(x).LocalPath; }
                    catch { }
                    return "";
                })
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleLinuxText(ClipboardMetaInfomation meta, CancellationToken token)
    {
        if (meta.Text is not null) return;
        var textBytes = await Clipboard.GetDataAsync(Format.Text).WaitAsync(token) as byte[];
        ArgumentNullException.ThrowIfNull(textBytes);
        meta.Text = Encoding.UTF8.GetString(textBytes);
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleLinuxHtml(ClipboardMetaInfomation meta, CancellationToken token)
    {
        var htmlBytes = await Clipboard.GetDataAsync(Format.TextHtml).WaitAsync(token) as byte[];
        ArgumentNullException.ThrowIfNull(htmlBytes);
        meta.Html = Encoding.UTF8.GetString(htmlBytes);
    }

    [SupportedOSPlatform("linux")]
    private static readonly string[] ImageTypeList = new[]
    {
        Format.ImagePng,
        Format.ImageJpeg,
        Format.ImageBmp,
    };

    [SupportedOSPlatform("linux")]
    private async Task HandleLinuxImage(ClipboardMetaInfomation meta, CancellationToken token)
    {
        meta.OriginalType = ClipboardMetaInfomation.ImageType;

        foreach (var imagetype in ImageTypeList)
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
            string text = "Unknow Image";
            var timeStamp = await Clipboard.GetDataAsync(Format.TimeStamp).WaitAsync(token) as byte[];
            if (timeStamp is not null)
            {
                text += BitConverter.ToInt32(timeStamp);
            }
            meta.Text = text;
        }
    }

    [SupportedOSPlatform("linux")]
    private async Task HandleGnomeFile(ClipboardMetaInfomation meta, CancellationToken token)
    {
        if (meta.Files is not null) return;

        var bytes = await Clipboard.GetDataAsync(Format.GnomeFiles).WaitAsync(token) as byte[];
        var str = Encoding.UTF8.GetString(bytes!);
        var pathList = str.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                            .Where(x => !string.IsNullOrEmpty(x)).ToArray();
        if (pathList.Length < 2) return;
        if (pathList[0] == "cut")
        {
            meta.Effects = DragDropEffects.Move;
        }

        meta.Files = pathList[1..].Select(x =>
        {
            try { return new Uri(x).LocalPath; }
            catch { }
            return "";
        })
        .Where(x => !string.IsNullOrEmpty(x))
        .ToArray();
    }

    [SupportedOSPlatform("linux")]
    private static readonly List<EffectsHandlerMapping> EffectsHandlerlist = new()
    {
        new EffectsHandlerMapping(Format.CompoundText, HandleCompoundText),
        new EffectsHandlerMapping(Format.KdeCutSelection, HandleKdeCutSelection),
    };

    [SupportedOSPlatform("linux")]
    private static async Task<DragDropEffects?> ParseEffects(IClipboard clipboard, string[] formats, CancellationToken token)
    {
        foreach (var handlerMapping in EffectsHandlerlist)
        {
            if (formats.Contains(handlerMapping.Key))
            {
                return await handlerMapping.Value.Invoke(clipboard, token);
            }
        }
        return null;
    }

    [SupportedOSPlatform("linux")]
    private static async Task<DragDropEffects?> HandleCompoundText(IClipboard clipboard, CancellationToken token)
    {
        var bytes = await clipboard.GetDataAsync(Format.CompoundText).WaitAsync(token) as byte[];
        var str = Encoding.UTF8.GetString(bytes!);
        string[] lines = str.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        if (lines.Length >= 3 && lines[1] == "cut")
        {
            return DragDropEffects.Move;
        }
        return null;
    }

    [SupportedOSPlatform("linux")]
    private static async Task<DragDropEffects?> HandleKdeCutSelection(IClipboard clipboard, CancellationToken token)
    {
        var bytes = await clipboard.GetDataAsync(Format.KdeCutSelection).WaitAsync(token) as byte[];
        var str = Encoding.UTF8.GetString(bytes!);
        if (str == "1")
        {
            return DragDropEffects.Move;
        }
        return null;
    }
}
