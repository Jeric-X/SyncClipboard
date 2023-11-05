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

    public virtual Task SetLocalClipboard(object obj, CancellationToken ctk)
    {
        if (OperatingSystem.IsLinux())
        {
            SetTimeStamp((DataObject)obj);
        }

        return App.Current.Clipboard.SetDataObjectAsync((IDataObject)obj).WaitAsync(ctk);
    }

    [SupportedOSPlatform("linux")]
    public static void SetTimeStamp(DataObject dataObject)
    {
        dataObject.Set(Format.TimeStamp, BitConverter.GetBytes(Environment.TickCount));
    }
}
