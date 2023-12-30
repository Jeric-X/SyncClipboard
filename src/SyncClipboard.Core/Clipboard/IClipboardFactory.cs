using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public interface IClipboardFactory
{
    Task<ClipboardMetaInfomation> GetMetaInfomation(CancellationToken ctk);
    Profile CreateProfileFromMeta(ClipboardMetaInfomation metaInfomation);
    Task<Profile> CreateProfileFromRemote(CancellationToken cancelToken);
}
