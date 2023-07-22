using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using Windows.ApplicationModel.DataTransfer;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    public abstract object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation);

    public void SetLocalClipboard(object obj)
    {
        Clipboard.SetContent(obj as DataPackage);
        Clipboard.Flush();
    }
}
