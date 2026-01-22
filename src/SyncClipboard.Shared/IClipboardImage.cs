namespace SyncClipboard.Shared;

public interface IClipboardImage
{
    public Task Save(string path, CancellationToken token);
    public Task<byte[]> SaveToBytes(CancellationToken token);
}
