using ImageMagick;

namespace SyncClipboard.Core.Utilities;

public class ClipboardImage : IClipboardImage
{
    private static readonly Lock SourceCacheLock = new();
    private static ClipboardImage? SourceCache;

    private static string? PngCacheContentHash;
    private static byte[]? PngCacheBytes;
    private static readonly SemaphoreSlim PngCacheSemaphore = new(1, 1);

    private readonly byte[] _imageBytes;
    private readonly bool _requiresOrientation;

    public string ContentHash { get; }

    /// <summary>Creates an image by taking ownership of the encoded bytes without copying them.</summary>
    /// <param name="imageBytes">
    /// Ownership is transferred on entry; callers must not modify or reuse the array as a writable buffer.
    /// </param>
    public ClipboardImage(byte[]? imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        _imageBytes = imageBytes;
        using var image = new MagickImage();
        image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
        image.Read(_imageBytes);
        _requiresOrientation = image.Orientation is not (OrientationType.Undefined or OrientationType.TopLeft);
        ContentHash = ClipboardImageHash.Compute(image, _imageBytes);
    }

    /// <summary>Creates or reuses an image, returning null if the input cannot be decoded.</summary>
    /// <param name="imageBytes">
    /// Ownership is transferred on entry, including on cache hits or failure;
    /// callers must not modify or reuse the array as a writable buffer.
    /// </param>
    public static ClipboardImage? TryCreateImage(byte[]? imageBytes)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(imageBytes);
            // Reuse the last owned source buffer before decoding; serialize concurrent cache misses.
            lock (SourceCacheLock)
            {
                if (SourceCache is not null && imageBytes.AsSpan().SequenceEqual(SourceCache._imageBytes))
                {
                    return SourceCache;
                }
                var image = new ClipboardImage(imageBytes);
                SourceCache = image;
                return image;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Creates or reuses an image on a background thread.</summary>
    /// <param name="imageBytes">
    /// Ownership is transferred when called; callers must not modify or reuse the array as a writable buffer,
    /// even after cancellation or failure.
    /// </param>
    /// <param name="token">Cancels waiting; decoding already in progress may continue using the transferred array.</param>
    public static Task<ClipboardImage?> TryCreateImageAsync(byte[]? imageBytes, CancellationToken token)
    {
        return Task.Run(() => TryCreateImage(imageBytes), token).WaitAsync(token);
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
        await PngCacheSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => PngCacheSemaphore.Release());
        if (PngCacheContentHash == ContentHash && PngCacheBytes is not null)
        {
            return PngCacheBytes;
        }

        // Decoding and encoding are synchronous native operations, so keep them off the UI thread.
        var bytes = await Task.Run(() =>
        {
            // Preserve original PNG chunks when no rotation/mirroring is needed for clipboard consumers.
            if (!_requiresOrientation && ClipboardImageHash.IsPng(_imageBytes))
            {
                return _imageBytes;
            }
            using var image = new MagickImage();
            image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
            image.Read(_imageBytes);
            // PNG consumers such as Avalonia do not apply orientation metadata themselves.
            ClipboardImageHash.ApplyOrientation(image);
            // PNG cannot represent CMYK. Convert only for output, never for the comparison fingerprint.
            if (image.ColorSpace is not (ColorSpace.sRGB or ColorSpace.RGB or ColorSpace.Gray or ColorSpace.LinearGray) &&
                image.GetColorProfile() is not null)
            {
                image.TransformColorSpace(ColorProfiles.SRGB);
                image.Depth = image.DetermineBitDepth();
            }
            if (image.Density.Units == DensityUnit.PixelsPerCentimeter)
            {
                // PNG truncates cm * 100: avoid 39.37 becoming 3936 instead of 3937 pixels per meter.
                image.Density = new Density(Math.BitIncrement(image.Density.X), Math.BitIncrement(image.Density.Y),
                    DensityUnit.PixelsPerCentimeter);
            }
            return image.ToByteArray(MagickFormat.Png);
        }, token);
        token.ThrowIfCancellationRequested();

        // Publish only a complete result; a canceled or failed encode must not poison the cache.
        PngCacheBytes = bytes;
        PngCacheContentHash = ContentHash;
        return bytes;
    }

    public override bool Equals(object? obj)
    {
        return obj is ClipboardImage other && GetType() == other.GetType() && ContentHash == other.ContentHash;
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(ContentHash);
    }
}
