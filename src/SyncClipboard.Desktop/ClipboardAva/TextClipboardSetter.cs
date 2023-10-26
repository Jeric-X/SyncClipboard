using Avalonia.Input;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System.Threading;
using System.Threading.Tasks;

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

    public override async Task SetLocalClipboard(object obj, CancellationToken ctk)
    {
        await App.Current.Clipboard.SetTextAsync(_text).WaitAsync(ctk);
    }
}
