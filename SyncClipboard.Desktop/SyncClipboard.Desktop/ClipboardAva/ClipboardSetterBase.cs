using Avalonia.Input;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using SyncClipboard.Desktop;

namespace SyncClipboard.WinUI3.ClipboardAva;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    public abstract object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation);

    public void SetLocalClipboard(object obj)
    {
        App.Current.Clipboard.SetDataObjectAsync((IDataObject)obj);
    }
}
