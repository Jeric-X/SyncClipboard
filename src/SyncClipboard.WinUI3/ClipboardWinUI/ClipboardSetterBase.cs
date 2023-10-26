using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    public abstract object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation);

    public Task SetLocalClipboard(object obj, CancellationToken ctk)
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
                return Task.CompletedTask;
            }
            catch { }
#pragma warning restore CC0004 // Catch block cannot be empty
        }
        return Task.CompletedTask;
    }
}
