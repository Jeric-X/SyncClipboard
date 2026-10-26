namespace SyncClipboard.Shared;

public interface IClipboardImage
{
    /// <summary>SHA256 of oriented pixels and equivalent image attributes, excluding allowlisted metadata.</summary>
    public string ContentHash { get; }

    public Task Save(string path, CancellationToken token);
    public Task<byte[]> SaveToBytes(CancellationToken token);
}
