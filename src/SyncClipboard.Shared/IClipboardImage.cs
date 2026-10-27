namespace SyncClipboard.Shared;

public interface IClipboardImage
{
    /// <summary>
    /// SHA256 of oriented pixels and decoded image attributes, excluding raw EXIF and allowlisted metadata.
    /// </summary>
    public string ContentHash { get; }

    public Task Save(string path, CancellationToken token);
    public Task<byte[]> SaveToBytes(CancellationToken token);
}
