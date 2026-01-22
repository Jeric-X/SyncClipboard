using ImageMagick;

namespace SyncClipboard.Core.Utilities;

public class ClipboardImage : IClipboardImage
{
    private static int CacheHash = 0;
    private static readonly MemoryStream Cache = new MemoryStream();
    private static readonly SemaphoreSlim CacheSemaphore = new SemaphoreSlim(1, 1);

    private readonly byte[] _imageBytes;

    public ClipboardImage(byte[]? imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes, nameof(imageBytes));
        _imageBytes = imageBytes;
    }

    public static ClipboardImage? TryCreateImage(byte[]? imageBytes)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(imageBytes, nameof(imageBytes));
            using MagickImage magickImage = new(imageBytes);
            return new ClipboardImage(imageBytes);
        }
        catch { }
        return null;
    }

    public async Task Save(string path, CancellationToken token)
    {
        var hash = _imageBytes.ListHashCode();
        if (await UseCacheAsync(hash, path, token))
            return;

        using MagickImage magickImage = new(_imageBytes);
        await CacheSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => CacheSemaphore.Release());

        CacheHash = hash;
        Cache.SetLength(0);
        Cache.Seek(0, SeekOrigin.Begin);
        await magickImage.WriteAsync(Cache, MagickFormat.Png, token);
        await WriteToFileAsync(path, token);
    }

    public Task<byte[]> SaveToBytes(CancellationToken token)
    {
        return Task.Run(async () =>
        {
            var hash = _imageBytes.ListHashCode();

            await CacheSemaphore.WaitAsync(token);
            using (var guard = new ScopeGuard(() => CacheSemaphore.Release()))
            {
                if (hash == CacheHash && Cache.Length > 0)
                {
                    return Cache.ToArray();
                }
            }

            using MagickImage magickImage = new(_imageBytes);
            using var ms = new MemoryStream();
            await magickImage.WriteAsync(ms, MagickFormat.Png, token);  // 只有文件操作是异步，转换操作是同步，需要用Task.Run包装
            var result = ms.ToArray();

            await CacheSemaphore.WaitAsync(token);
            using (var guard = new ScopeGuard(() => CacheSemaphore.Release()))
            {
                CacheHash = hash;
                Cache.SetLength(0);
                Cache.Seek(0, SeekOrigin.Begin);
                await Cache.WriteAsync(result, token);
                Cache.Seek(0, SeekOrigin.Begin);
            }
            return result;
        }, token).WaitAsync(token);
    }

    private static async Task<bool> UseCacheAsync(int hash, string path, CancellationToken token)
    {
        await CacheSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => CacheSemaphore.Release());

        if (hash == CacheHash)
        {
            await WriteToFileAsync(path, token);
            return true;
        }
        return false;
    }

    private static async Task WriteToFileAsync(string file, CancellationToken token)
    {
        using var fileStream = File.Create(file);
        Cache.Seek(0, SeekOrigin.Begin);
        await Cache.CopyToAsync(fileStream, token);
    }

    public override bool Equals(object? obj)
    {
        if (obj == null || GetType() != obj.GetType())
        {
            return false;
        }

        return _imageBytes.SequenceEqual(((ClipboardImage)obj)._imageBytes);
    }

    public override int GetHashCode()
    {
        return _imageBytes.ListHashCode();
    }
}