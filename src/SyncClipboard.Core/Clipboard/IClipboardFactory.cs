using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public interface IClipboardFactory
{
    Task<ClipboardMetaInfomation> GetMetaInfomation(CancellationToken ctk);

    /// <summary>Reads a change notification, returning null if a failed read is superseded by a newer notification.</summary>
    Task<ClipboardMetaInfomation?> GetMetaInfomation(ClipboardChangeToken changeToken, CancellationToken ctk);

    Task<Profile> CreateProfileFromMeta(ClipboardMetaInfomation metaInfomation, CancellationToken ctk);
    Task<Profile> CreateProfileFromMeta(ClipboardMetaInfomation metaInfomation, bool contentControl, CancellationToken ctk);
    Task<Profile> CreateProfileFromLocal(CancellationToken ctk);
    void SetClipboardOwner(ClipboardMetaInfomation meta);
}
