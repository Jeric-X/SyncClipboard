using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    protected abstract Task<DataPackage> CreatePackage(ClipboardMetaInfomation metaInfomation);

    private static async Task SetPackageToClipboard(DataPackage package, CancellationToken ctk)
    {
        ctk.ThrowIfCancellationRequested();
        Clipboard.SetContent(package);
        // Clipboard.SetContent() still occupies the system clipboard after calling
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(50, CancellationToken.None);
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

    public async Task SetLocalClipboard(ClipboardMetaInfomation metaInfomation, CancellationToken ctk)
    {
        await ClipboardSetterBase<ProfileType>.SetPackageToClipboard(await CreatePackage(metaInfomation), ctk);
    }
}
