using SyncClipboard.Core.Clipboard;
using System.Threading.Tasks;
using System.Threading;

namespace SyncClipboard.Service;

public interface IClipboardFactory
{
    MetaInfomation GetMetaInfomation();
    Profile CreateProfile(MetaInfomation metaInfomation = default);
    Task<Profile> CreateProfileFromRemote(CancellationToken cancelToken);
}
