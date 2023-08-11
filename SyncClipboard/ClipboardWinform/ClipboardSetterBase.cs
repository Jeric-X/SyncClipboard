using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System.Windows.Forms;

namespace SyncClipboard.ClipboardWinform;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    public abstract object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation);

    public void SetLocalClipboard(object obj)
    {
        Clipboard.SetDataObject(obj as DataObject, true); ;
    }
}
