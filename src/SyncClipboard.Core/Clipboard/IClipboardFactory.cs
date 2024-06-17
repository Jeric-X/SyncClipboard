using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public interface IClipboardFactory
{
    Task<ClipboardMetaInfomation> GetMetaInfomation(CancellationToken ctk);
    Task<Profile> CreateProfileFromMeta(ClipboardMetaInfomation metaInfomation, CancellationToken ctk);
    Task<Profile> CreateProfileFromRemote(CancellationToken cancelToken);
    Task<Profile> CreateProfileFromLocal(CancellationToken ctk);
}
