using SyncClipboard.Core;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    public abstract Task FillPackage(object package, ClipboardMetaInfomation metaInfomation);

    private static async Task SetPackageToClipboard(DataPackage package, CancellationToken ctk)
    {
        ctk.ThrowIfCancellationRequested();
        using var nativeClipboardAccessGuard = await NativeClipboardAccess.AcquireAsync(ctk);
        Clipboard.SetContent(package);
        // Clipboard.SetContent() still occupies the system clipboard after calling
        for (int i = 0; i < 5; i++)
        {
#pragma warning disable CC0004 // Catch block cannot be empty
            await Task.Delay(50, CancellationToken.None);
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
        await AppCore.Current.Logger.WriteAsync("Clip Setter", "Clipboard setted, meta: " + metaInfomation);
        var package = new DataPackage();
        await FillPackage(package, metaInfomation);
        await SetPackageToClipboard(package, ctk);
    }
}
