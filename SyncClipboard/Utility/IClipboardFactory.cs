using SyncClipboard.Core.Clipboard;

namespace SyncClipboard.Service;

public interface IClipboardFactory
{
    public MetaInfomation GetMetaInfomation();
    public Profile CreateProfile(MetaInfomation metaInfomation = default);
}
