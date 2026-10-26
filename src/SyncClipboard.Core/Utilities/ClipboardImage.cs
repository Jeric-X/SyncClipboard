using ImageMagick;

namespace SyncClipboard.Core.Utilities;

public class ClipboardImage : IClipboardImage
{
    private static string? CacheHash;
    private static byte[]? CacheBytes;
    private static readonly SemaphoreSlim CacheSemaphore = new(1, 1);

    private readonly byte[] _imageBytes;
    private readonly bool _requiresOrientation;

    public string ContentHash { get; }

    public ClipboardImage(byte[]? imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        _imageBytes = (byte[])imageBytes.Clone();
        using var image = new MagickImage();
        image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
        image.Read(_imageBytes);
        _requiresOrientation = image.Orientation is not (OrientationType.Undefined or OrientationType.TopLeft);
        ContentHash = ClipboardImageHash.Compute(image, _imageBytes);
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
        if (CacheHash == ContentHash && CacheBytes is not null)
        {
            return CacheBytes;
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
        CacheBytes = bytes;
        CacheHash = ContentHash;
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
