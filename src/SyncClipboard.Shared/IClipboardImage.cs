namespace SyncClipboard.Shared;

public interface IClipboardImage
{
    /// <summary>SHA256 of the normalized image dimensions and RGBA16 pixels, independent of file encoding.</summary>
    public string PixelHash { get; }

    public Task Save(string path, CancellationToken token);
    public Task<byte[]> SaveToBytes(CancellationToken token);
}
