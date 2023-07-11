using SyncClipboard.Service;
#nullable enable
namespace SyncClipboard.Core.Clipboard;

public interface IClipboardSetter<out ProfileType> where ProfileType : Profile
{
    object CreateClipboardObjectContainer(Profile profile);
    void SetLocalClipboard(object obj);
}
