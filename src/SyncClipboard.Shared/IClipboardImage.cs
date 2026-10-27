namespace SyncClipboard.Shared;

public interface IClipboardImage
{
    /// <summary>
    /// SHA256 of the encoded output's pixels and attributes, excluding allowlisted metadata.
    /// Multi-frame images use the original file's SHA256.
    /// </summary>
    public string ContentHash { get; }

    public Task Save(string path, CancellationToken token);
    public Task<byte[]> SaveToBytes(CancellationToken token);
}
