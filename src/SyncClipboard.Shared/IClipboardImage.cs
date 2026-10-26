namespace SyncClipboard.Shared;

public interface IClipboardImage
{
    /// <summary>SHA256 of decoded pixels and image attributes, excluding explicitly allowlisted metadata.</summary>
    public string ContentHash { get; }

    public Task Save(string path, CancellationToken token);
    public Task<byte[]> SaveToBytes(CancellationToken token);
}
