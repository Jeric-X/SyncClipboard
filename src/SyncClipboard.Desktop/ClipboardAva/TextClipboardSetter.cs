using Avalonia.Input;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class TextClipboardSetter : ClipboardSetterBase<TextProfile>
{
    private string _text = "";
    public override object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation)
    {
        _text = metaInfomation?.Text ?? "";

        var dataObject = new DataObject();
        if (OperatingSystem.IsLinux())
        {
            var utf8Text = Encoding.UTF8.GetBytes(_text);
            dataObject.Set(Format.Text, utf8Text);
            dataObject.Set("text/plain", utf8Text);
            dataObject.Set("text/plain;charset=utf-8", utf8Text);
        }
        return dataObject;
    }

    public override async Task SetLocalClipboard(object obj, CancellationToken ctk)
    {
        if (OperatingSystem.IsLinux())
        {
            await base.SetLocalClipboard(obj, ctk);
        }
        else
        {
            await App.Current.Clipboard.SetTextAsync(_text).WaitAsync(ctk);
        }
    }
}
