using Avalonia.Input;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class TextClipboardSetter : ClipboardSetterBase<TextProfile>
{
    public override Task FillPackage(object package, ClipboardMetaInfomation metaInfomation)
    {
        if (package is not DataTransfer dataTransfer)
        {
            return Task.CompletedTask;
        }

        var text = metaInfomation?.Text ?? "";
        var item = new DataTransferItem();

        if (OperatingSystem.IsLinux())
        {
            var utf8Text = Encoding.UTF8.GetBytes(text);
            item.Set(DataFormat.CreateBytesPlatformFormat(Format.TEXT), utf8Text);
            item.Set(DataFormat.CreateBytesPlatformFormat("text/plain"), utf8Text);
            item.Set(DataFormat.CreateBytesPlatformFormat("text/plain;charset=utf-8"), utf8Text);
            item.Set(DataFormat.CreateStringPlatformFormat(Format.Utf8String), text);
        }
        else
        {
            // macOS and Windows use simpler text format
            item.SetText(text);
        }

        dataTransfer.Add(item);
        return Task.CompletedTask;
    }

    public override Task SetLocalClipboard(ClipboardMetaInfomation metaInfomation, CancellationToken ctk)
    {
        if (OperatingSystem.IsLinux())
        {
            return base.SetLocalClipboard(metaInfomation, ctk);
        }
        return App.Current.Clipboard.SetTextAsync(metaInfomation?.Text ?? "").WaitAsync(ctk);
    }
}
