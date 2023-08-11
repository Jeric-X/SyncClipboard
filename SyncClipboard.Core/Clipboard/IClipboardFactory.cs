using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public interface IClipboardFactory
{
    ClipboardMetaInfomation GetMetaInfomation();
    Profile CreateProfile(ClipboardMetaInfomation? metaInfomation = default);
    Task<Profile> CreateProfileFromRemote(CancellationToken cancelToken);
}
