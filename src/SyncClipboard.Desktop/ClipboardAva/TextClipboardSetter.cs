using Avalonia.Input;
using Avalonia.Threading;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class TextClipboardSetter : ClipboardSetterBase<TextProfile>
{
    private string? _text;
    public override object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation)
    {
        _text = metaInfomation.Text;
        var dataObject = new DataObject();
        dataObject.Set("Text", metaInfomation?.Text ?? "");
        return dataObject;
    }

    public override void SetLocalClipboard(object obj)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            App.Current.Clipboard.SetTextAsync(_text).Wait();
        }
        else
        {
            Dispatcher.UIThread.Invoke(
                () => App.Current.Clipboard.SetTextAsync(_text),
                DispatcherPriority.Send
            ).Wait();
        }
    }
}
