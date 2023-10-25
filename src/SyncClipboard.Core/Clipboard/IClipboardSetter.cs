using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public interface IClipboardSetter<out ProfileType> where ProfileType : Profile
{
    object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation);
    Task SetLocalClipboard(object obj, CancellationToken ctk);
}
