using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    public abstract Task FillPackage(object package, ClipboardMetaInfomation metaInfomation);

    private static async Task SetPackageToClipboard(DataTransfer transfer, CancellationToken ctk)
    {
        if (OperatingSystem.IsLinux())
        {
            SetTimeStamp(transfer);
        }

        await ClipboardFactory._semaphoreSlim.WaitAsync(ctk);
        try
        {
            await App.Current.Clipboard.SetDataAsync(transfer).WaitAsync(ctk);
        }
        catch { }
        finally
        {
            ClipboardFactory._semaphoreSlim.Release();
        }

        App.Current.Services.GetRequiredService<ClipboardListener>().TriggerClipboardChangedEvent();
    }

    [SupportedOSPlatform("linux")]
    public static void SetTimeStamp(DataTransfer dataTransfer)
    {
        var item = new DataTransferItem();
        item.Set(DataFormat.CreateBytesPlatformFormat(Format.TimeStamp), Encoding.UTF8.GetBytes($"{Environment.TickCount}{Environment.NewLine}"));
        dataTransfer.Add(item);
    }

    public virtual async Task SetLocalClipboard(ClipboardMetaInfomation metaInfomation, CancellationToken ctk)
    {
        var dataTransfer = new DataTransfer();
        await FillPackage(dataTransfer, metaInfomation);
        await ClipboardSetterBase<ProfileType>.SetPackageToClipboard(dataTransfer, ctk);
    }
}
