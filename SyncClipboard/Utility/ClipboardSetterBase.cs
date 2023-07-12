using SyncClipboard.Core.Clipboard;
using SyncClipboard.Service;
using System.Windows.Forms;

namespace SyncClipboard.Utility;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    public abstract object CreateClipboardObjectContainer(MetaInfomation metaInfomation);

    public void SetLocalClipboard(object obj)
    {
        Clipboard.SetDataObject(obj as DataObject, true); ;
    }
}
