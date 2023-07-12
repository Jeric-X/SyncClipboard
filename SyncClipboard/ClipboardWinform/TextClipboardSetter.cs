using SyncClipboard.Core.Clipboard;
using System.Windows.Forms;

namespace SyncClipboard.ClipboardWinform;

internal class TextClipboardSetter : ClipboardSetterBase<TextProfile>
{
    public override object CreateClipboardObjectContainer(MetaInfomation metaInfomation)
    {
        var dataObject = new DataObject();
        dataObject.SetData(DataFormats.Text, metaInfomation.Text);
        return dataObject;
    }
}
