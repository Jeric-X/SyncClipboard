using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;
using System;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    public abstract Task FillPackage(object package, ClipboardMetaInfomation metaInfomation);

    private static async Task SetPackageToClipboard(AutoDisposeDataTransfer dataTransfer, CancellationToken ctk)
    {
        ClipboardWriterSelector writer;
        try
        {
            if (OperatingSystem.IsLinux())
            {
                SetTimeStamp(dataTransfer.Data);
            }
            ctk.ThrowIfCancellationRequested();
            writer = App.Current.Services.GetRequiredService<ClipboardWriterSelector>();
            await NativeClipboardAccess.Semaphore.WaitAsync(ctk);
        }
        catch
        {
            dataTransfer.Dispose();
            throw;
        }

        try
        {
            // 写入器接管数据包的所有权，提交给平台前失败也由写入器负责清理。
            await writer.SetDataAsync(dataTransfer, ctk);
        }
        finally
        {
            NativeClipboardAccess.Semaphore.Release();
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
        var dataTransfer = new AutoDisposeDataTransfer(new DataTransfer());
        try
        {
            await FillPackage(dataTransfer.Data, metaInfomation);
        }
        catch
        {
            dataTransfer.Dispose();
            throw;
        }

        await SetPackageToClipboard(dataTransfer, ctk);
    }
}
