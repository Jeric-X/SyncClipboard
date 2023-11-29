using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public interface IClipboardSetter<out ProfileType> where ProfileType : Profile
{
    Task SetLocalClipboard(ClipboardMetaInfomation metaInfomation, CancellationToken ctk);
}
