using Avalonia.Input;
using Avalonia.Input.Platform;
using SyncClipboard.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class TextClipboardSetter : ClipboardSetterBase<TextProfile>
{
    protected override Task WriteWithWlClipboardAsync(
        WlClipboardWriter writer, ClipboardMetaInfomation metaInfomation, CancellationToken token) =>
        writer.WriteTextAsync(metaInfomation.Text ?? "", token);

    public override Task FillPackage(object package, ClipboardMetaInfomation metaInfomation)
    {
        if (package is not DataTransfer dataTransfer)
        {
            return Task.CompletedTask;
        }

        var text = metaInfomation?.Text ?? "";
        var item = new DataTransferItem();

        item.SetText(text);

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
