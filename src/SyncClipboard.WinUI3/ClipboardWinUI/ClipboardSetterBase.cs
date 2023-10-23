using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    public abstract object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation);

    public void SetLocalClipboard(object obj)
    {
        Clipboard.SetContent(obj as DataPackage);

        // Clipboard.SetContent() still occupies the system clipboard after calling
        for (int i = 0; i < 5; i++)
        {
            Thread.Sleep(50);
#pragma warning disable CC0004 // Catch block cannot be empty
            try
            {
                Clipboard.Flush();
                return;
            }
            catch { }
#pragma warning restore CC0004 // Catch block cannot be empty
        }
    }
}
