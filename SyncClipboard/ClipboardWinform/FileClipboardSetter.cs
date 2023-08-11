using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System.Windows.Forms;

namespace SyncClipboard.ClipboardWinform;

internal class FileClipboardSetter : ClipboardSetterBase<FileProfile>
{
    public override object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation)
    {
        var dataObject = new DataObject();
        dataObject.SetFileDropList(new System.Collections.Specialized.StringCollection { metaInfomation.Files[0] });
        return dataObject;
    }
}
