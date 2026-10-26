using ImageMagick;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SyncClipboard.Core.Utilities;

public class ClipboardImage : IClipboardImage
{
    private static string? CacheHash;
    private static byte[]? CacheBytes;
    private static readonly SemaphoreSlim CacheSemaphore = new(1, 1);

    private readonly byte[] _imageBytes;

    public string PixelHash { get; }

    public ClipboardImage(byte[]? imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        _imageBytes = (byte[])imageBytes.Clone();
        using var image = new MagickImage(_imageBytes);
        Normalize(image);
        PixelHash = ComputePixelHash(image);
    }

    public static ClipboardImage? TryCreateImage(byte[]? imageBytes)
    {
        try
        {
            return new ClipboardImage(imageBytes);
        }
        catch { }
        return null;
    }

    public static Task<ClipboardImage?> TryCreateImageAsync(byte[]? imageBytes, CancellationToken token)
    {
        return Task.Run(() => TryCreateImage(imageBytes), token).WaitAsync(token);
    }

    private static void Normalize(MagickImage image)
    {
        // Orientation and color profiles affect the displayed pixels; apply them before ignoring metadata.
        image.AutoOrient();
        if (image.GetColorProfile() is not null)
        {
            image.TransformColorSpace(ColorProfiles.SRGB);
        }
        else if (image.GetAttribute("png:sRGB") is null && image.ColorSpace is ColorSpace.sRGB or ColorSpace.Gray &&
            image.Gamma > 0 && Math.Round(image.Gamma * 100000) != 45455)
        {
            // Without ICC or explicit sRGB, a nonstandard PNG gAMA defines a power transfer function.
            // Decode it to linear light, then encode sRGB. Only color channels change, never alpha.
            image.GammaCorrect(image.Gamma, Channels.RGB);
            image.SetAttribute("colorspace", "RGB");
            image.ColorSpace = ColorSpace.sRGB;
        }
        else
        {
            image.ColorSpace = ColorSpace.sRGB;
        }
        image.SetAttribute("gamma", "0.45454545454545453");
    }

    private static string ComputePixelHash(MagickImage image)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> dimensions = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(dimensions, image.Width);
        BinaryPrimitives.WriteUInt32LittleEndian(dimensions[4..], image.Height);
        hash.AppendData(dimensions);

        using var pixels = image.GetPixels();
        for (int y = 0; y < image.Height; y++)
        {
            // Export one row at a time, preserving Q16 precision and alpha without another full image buffer.
            var row = pixels.ToShortArray(0, y, image.Width, 1, PixelMapping.RGBA)!;
            if (!BitConverter.IsLittleEndian)
            {
                BinaryPrimitives.ReverseEndianness(row, row);
            }
            hash.AppendData(MemoryMarshal.AsBytes(row.AsSpan()));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public async Task Save(string path, CancellationToken token)
    {
        var bytes = await GetPngBytes(token);
        await File.WriteAllBytesAsync(path, bytes, token);
    }

    public async Task<byte[]> SaveToBytes(CancellationToken token)
    {
        var bytes = await GetPngBytes(token);
        return (byte[])bytes.Clone();
    }

    private async Task<byte[]> GetPngBytes(CancellationToken token)
    {
        await CacheSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => CacheSemaphore.Release());
        if (CacheHash == PixelHash && CacheBytes is not null)
        {
            return CacheBytes;
        }

        // Decoding and encoding are synchronous native operations, so keep them off the UI thread.
        var bytes = await Task.Run(() =>
        {
            using var image = new MagickImage(_imageBytes);
            Normalize(image);
            // Color conversion can introduce precision beyond the source file's bit depth.
            image.Depth = image.DetermineBitDepth();
            return image.ToByteArray(MagickFormat.Png);
        }, token);
        token.ThrowIfCancellationRequested();

        // Publish only a complete result; a canceled or failed encode must not poison the cache.
        CacheBytes = bytes;
        CacheHash = PixelHash;
        return bytes;
    }

    public override bool Equals(object? obj)
    {
        return obj is ClipboardImage other && GetType() == other.GetType() && PixelHash == other.PixelHash;
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(PixelHash);
    }
}
