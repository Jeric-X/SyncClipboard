namespace SyncClipboard.Core.Clipboard;

public interface IClipboardFactory
{
    MetaInfomation GetMetaInfomation();
    Profile CreateProfile(MetaInfomation? metaInfomation = default);
    Task<Profile> CreateProfileFromRemote(CancellationToken cancelToken);
}
