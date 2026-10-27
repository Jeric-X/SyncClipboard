using ImageMagick;
using System.Security.Cryptography;

namespace SyncClipboard.Core.Utilities;

public class ClipboardImage : IClipboardImage
{
    private static readonly Lock SourceCacheLock = new();
    private static ClipboardImage? SourceCache;

    private readonly byte[] _imageBytes;
    private readonly byte[] _encodedBytes;

    public string ContentHash { get; }

    /// <summary>Creates an image by taking ownership of the encoded bytes without copying them.</summary>
    /// <param name="imageBytes">
    /// Ownership is transferred on entry; callers must not modify or reuse the array as a writable buffer.
    /// </param>
    public ClipboardImage(byte[]? imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        _imageBytes = imageBytes;
        using (var image = ReadImage(imageBytes))
        {
            if (IsMultiFrame(image, imageBytes))
            {
                _encodedBytes = imageBytes;
                ContentHash = Convert.ToHexString(SHA256.HashData(imageBytes));
                return;
            }
            _encodedBytes = EncodePng(image);
        }
        // Compare the pixels and properties actually saved, including the encoder's metadata decisions.
        using var encoded = ReadImage(_encodedBytes);
        ContentHash = ClipboardImageHash.Compute(encoded);
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

    public Task Save(string path, CancellationToken token)
    {
        return File.WriteAllBytesAsync(path, _encodedBytes, token);
    }

    public Task<byte[]> SaveToBytes(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult((byte[])_encodedBytes.Clone());
    }

    private static MagickImage ReadImage(byte[] source)
    {
        var image = new MagickImage();
        try
        {
            image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
            image.Read(source);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static bool IsMultiFrame(MagickImage image, byte[] source)
    {
        // PNG ReadCollection exposes only the default APNG image, so also check its animation control chunk.
        return image.GetAttribute("png:acTL") is not null ||
            (image.Format is not (MagickFormat.Png or MagickFormat.Bmp or MagickFormat.Bmp2 or MagickFormat.Bmp3) &&
                MagickImageInfo.ReadCollection(source).Skip(1).Any());
    }

    internal static byte[] EncodeImage(byte[] source)
    {
        using var image = ReadImage(source);
        return IsMultiFrame(image, source) ? source : EncodePng(image);
    }

    private static byte[] EncodePng(MagickImage image)
    {
        // Expanded RGBA/gray-alpha PNG supports 8 or 16 bits per channel.
        uint depth = image.Depth <= 8 ? 8u : 16u;
        // PNG consumers such as Avalonia do not apply orientation metadata themselves.
        ClipboardImageHash.ApplyOrientation(image);
        PreparePngColorSpace(image);
        ClipboardImageHash.PreparePngMetadata(image);
        image.Depth = depth;
        image.Settings.Interlace = Interlace.NoInterlace;
        image.Settings.SetDefine(MagickFormat.Png, "bit-depth", (int)image.Depth);
        image.Settings.SetDefine(MagickFormat.Png, "color-type",
            image.ColorSpace is ColorSpace.Gray or ColorSpace.LinearGray ? 4 : 6);
        image.Settings.SetDefine(MagickFormat.Png, "compression-level", 6);
        image.Settings.SetDefine(MagickFormat.Png, "compression-filter", 1);
        image.Settings.SetDefine(MagickFormat.Png, "compression-strategy", 0);
        image.Settings.SetDefine(MagickFormat.Png, "exclude-chunk", "date,tIME,eXIf");
        image.Settings.SetDefine(MagickFormat.Png, "include-chunk", "cHRM,bKGD,gAMA");
        var bytes = image.ToByteArray(MagickFormat.Png);
        if (image.ColorSpace is ColorSpace.RGB or ColorSpace.LinearGray)
        {
            // The encoder can replace a linear color description. Use its resulting chunk layout consistently.
            using var encoded = ReadImage(bytes);
            if (encoded.ColorSpace != image.ColorSpace)
            {
                return EncodePng(encoded);
            }
        }
        return bytes;
    }

    private static void PreparePngColorSpace(MagickImage image)
    {
        // PNG cannot represent CMYK. The fingerprint will be computed from the converted output.
        if (image.ColorSpace is not (ColorSpace.sRGB or ColorSpace.RGB or ColorSpace.Gray or ColorSpace.LinearGray) &&
            image.GetColorProfile() is not null)
        {
            image.TransformColorSpace(ColorProfiles.SRGB);
        }
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
