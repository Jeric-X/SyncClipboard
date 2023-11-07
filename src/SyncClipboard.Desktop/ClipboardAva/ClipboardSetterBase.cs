using Avalonia.Input;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal abstract class ClipboardSetterBase<ProfileType> : IClipboardSetter<ProfileType> where ProfileType : Profile
{
    public abstract object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation);

    public virtual async Task SetLocalClipboard(object obj, CancellationToken ctk)
    {
        if (OperatingSystem.IsLinux())
        {
            SetTimeStamp((DataObject)obj);
        }

        await ClipboardFactory._semaphoreSlim.WaitAsync(ctk);
        try
        {
            await App.Current.Clipboard.SetDataObjectAsync((IDataObject)obj).WaitAsync(ctk);
            await Task.Delay(200, ctk);
        }
        catch { }
        finally
        {
            ClipboardFactory._semaphoreSlim.Release();
        }
    }

    [SupportedOSPlatform("linux")]
    public static void SetTimeStamp(DataObject dataObject)
    {
        dataObject.Set(Format.TimeStamp, BitConverter.GetBytes(Environment.TickCount));
    }
}
