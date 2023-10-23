using Avalonia.Input;
using Avalonia.Threading;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.ClipboardAva;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    public abstract object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation);

    public virtual void SetLocalClipboard(object obj)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            App.Current.Clipboard.SetDataObjectAsync((IDataObject)obj).Wait();
        }
        else
        {
            Dispatcher.UIThread.Invoke(
                () => App.Current.Clipboard.SetDataObjectAsync((IDataObject)obj),
                DispatcherPriority.Send
            ).Wait();
        }
    }
}
