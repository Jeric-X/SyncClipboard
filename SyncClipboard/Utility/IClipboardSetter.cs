using SyncClipboard.Service;
#nullable enable
namespace SyncClipboard.Core.Clipboard;

public interface IClipboardSetter<out ProfileType> where ProfileType : Profile
{
    object CreateClipboardObjectContainer(MetaInfomation metaInfomation);
    void SetLocalClipboard(object obj);
}
