using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IClipboardChangingListener
{
    event Action<ClipboardMetaInfomation> Changed;
}
