using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public interface IClipboardSetter<out ProfileType> : IClipboardSetter where ProfileType : Profile
{
}

public interface IClipboardSetter
{
    Task SetLocalClipboard(ClipboardMetaInfomation metaInfomation, CancellationToken ctk);
}
