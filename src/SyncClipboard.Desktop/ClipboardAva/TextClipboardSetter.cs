using Avalonia.Input;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class TextClipboardSetter : ClipboardSetterBase<TextProfile>
{
    [SupportedOSPlatform("linux")]
    protected override DataObject CreatePackage(ClipboardMetaInfomation metaInfomation)
    {
        var utf8Text = Encoding.UTF8.GetBytes(metaInfomation?.Text ?? "");
        var dataObject = new DataObject();
        dataObject.Set(Format.TEXT, utf8Text);
        dataObject.Set("text/plain", utf8Text);
        dataObject.Set("text/plain;charset=utf-8", utf8Text);
        return dataObject;
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
