using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal class TextClipboardSetter : ClipboardSetterBase<TextProfile>
{
    protected override Task<DataPackage> CreatePackage(ClipboardMetaInfomation metaInfomation)
    {
        var dataObject = new DataPackage();
        dataObject.SetText(metaInfomation.Text);
        return Task.FromResult(dataObject);
    }
}
