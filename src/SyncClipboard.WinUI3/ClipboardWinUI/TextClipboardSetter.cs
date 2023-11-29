using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using Windows.ApplicationModel.DataTransfer;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal class TextClipboardSetter : ClipboardSetterBase<TextProfile>
{
    protected override DataPackage CreatePackage(ClipboardMetaInfomation metaInfomation)
    {
        var dataObject = new DataPackage();
        dataObject.SetText(metaInfomation.Text);
        return dataObject;
    }
}
