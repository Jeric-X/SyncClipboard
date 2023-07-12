using SyncClipboard.Core.Clipboard;
using SyncClipboard.Service;
using System.Windows.Forms;

namespace SyncClipboard.Utility
{
    internal class TextClipboardSetter : ClipboardSetterBase<TextProfile>
    {
        public override object CreateClipboardObjectContainer(MetaInfomation metaInfomation)
        {
            var dataObject = new DataObject();
            dataObject.SetData(DataFormats.Text, metaInfomation.Text);
            return dataObject;
        }
    }
}
