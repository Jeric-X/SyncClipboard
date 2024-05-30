using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    protected abstract DataObject CreatePackage(ClipboardMetaInfomation metaInfomation);

    private static async Task SetPackageToClipboard(DataObject obj, CancellationToken ctk)
    {
        if (OperatingSystem.IsLinux())
        {
            SetTimeStamp(obj);
        }

        await ClipboardFactory._semaphoreSlim.WaitAsync(ctk);
        try
        {
            await App.Current.Clipboard.SetDataObjectAsync(obj).WaitAsync(ctk);
        }
        catch { }
        finally
        {
            ClipboardFactory._semaphoreSlim.Release();
        }

        App.Current.Services.GetRequiredService<ClipboardListener>().TriggerClipboardChangedEvent();
    }

    [SupportedOSPlatform("linux")]
    public static void SetTimeStamp(DataObject dataObject)
    {
        dataObject.Set(Format.TimeStamp, BitConverter.GetBytes(Environment.TickCount));
    }

    public virtual Task SetLocalClipboard(ClipboardMetaInfomation metaInfomation, CancellationToken ctk)
    {
        return ClipboardSetterBase<ProfileType>.SetPackageToClipboard(CreatePackage(metaInfomation), ctk);
    }
}
