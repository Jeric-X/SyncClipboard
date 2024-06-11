using ImageMagick;
using SyncClipboard.Abstract;

namespace SyncClipboard.Core.Utilities;

public class ClipboardImage : IClipboardImage
{
    private static int CacheHash = 0;
    private static readonly MemoryStream Cache = new MemoryStream();

    private readonly byte[] _imageBytes;

    public ClipboardImage(byte[]? imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes, nameof(imageBytes));
        _imageBytes = imageBytes;
    }

    public void Save(string path)
    {
        var hash = _imageBytes.ListHashCode();
        if (UseCache(hash, path))
            return;

        using MagickImage magickImage = new(_imageBytes);
        lock (Cache)
        {
            CacheHash = hash;
            Cache.SetLength(0);
            Cache.Seek(0, SeekOrigin.Begin);
            magickImage.Write(Cache, MagickFormat.Png);
            WriteToFile(path);
        }
    }

    private static bool UseCache(int hash, string path)
    {
        lock (Cache)
        {
            if (hash == CacheHash)
            {
                WriteToFile(path);
                return true;
            }
        }
        return false;
    }

    private static void WriteToFile(string file)
    {
        using var fileStream = File.Create(file);
        Cache.Seek(0, SeekOrigin.Begin);
        Cache.CopyTo(fileStream);
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