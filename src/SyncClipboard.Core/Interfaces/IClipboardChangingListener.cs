using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public delegate void ClipboardChangedDelegate(ClipboardMetaInfomation meta, Profile profile);

public interface IClipboardChangingListener
{
    event ClipboardChangedDelegate Changed;
}
