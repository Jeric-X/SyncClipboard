using SyncClipboard.Core.Clipboard;
using System.Windows.Forms;

namespace SyncClipboard.ClipboardWinform;

internal class FileClipboardSetter : ClipboardSetterBase<FileProfile>
{
    public override object CreateClipboardObjectContainer(MetaInfomation metaInfomation)
    {
        var dataObject = new DataObject();
        dataObject.SetFileDropList(new System.Collections.Specialized.StringCollection { metaInfomation.Files[0] });
        return dataObject;
    }
}
