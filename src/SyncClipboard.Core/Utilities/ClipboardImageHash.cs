using ImageMagick;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities;

internal static class ClipboardImageHash
{
    // Exact attribute names, never prefixes: unknown attributes and all profiles participate in comparison.
    private static readonly HashSet<string> IgnoredAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Title", "Author", "Description", "Copyright", "Creation Time", "Software",
        "Disclaimer", "Warning", "Source", "Comment", "date:create", "date:modify", "date:timestamp",
        "png:tIME", "png:pHYs",
        // Decoder diagnostics describe storage, or duplicate properties hashed below.
        "png:IHDR.bit-depth-orig", "png:IHDR.bit_depth", "png:IHDR.color-type-orig", "png:IHDR.color_type",
        "png:IHDR.interlace_method", "png:IHDR.width,height", "png:PLTE.number_colors", "png:text",
        "png:bKGD", "png:cHRM", "png:gAMA", "tiff:endian", "tiff:rows-per-strip", "tiff:photometric", "tiff:alpha"
    };

    internal static bool IsPng(ReadOnlySpan<byte> data) => data.StartsWith((ReadOnlySpan<byte>)[137, 80, 78, 71, 13, 10, 26, 10]);

    internal static string Compute(MagickImage image, ReadOnlySpan<byte> source)
    {
        // ClipboardImage decodes one frame. Do not merge multi-frame containers based on that frame alone.
        if (image.Format is not (MagickFormat.Png or MagickFormat.Bmp or MagickFormat.Bmp2 or MagickFormat.Bmp3) &&
            MagickImageInfo.ReadCollection(source).Skip(1).Any())
        {
            return Convert.ToHexString(SHA256.HashData(source));
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // No AutoOrient, color-space conversion or gamma correction: retain original pixel interpretation.
        Append(hash, JsonSerializer.SerializeToUtf8Bytes(new
        {
            image.Width,
            image.Height,
            image.ColorSpace,
            image.Gamma,
            image.Chromaticity,
            Orientation = image.Orientation == OrientationType.Undefined ? OrientationType.TopLeft : image.Orientation,
            Background = image.BackgroundColor?.ToString(),
            image.RenderingIntent,
            Page = image.Page.ToString(),
            image.AnimationDelay,
            image.AnimationIterations,
            image.AnimationTicksPerSecond,
            image.GifDisposeMethod,
            PixelAspectRatio = GetAspectRatio(image.Density)
        }));

        foreach (string name in image.AttributeNames.Order(StringComparer.Ordinal))
        {
            if (!IgnoredAttributes.Contains(name))
            {
                Append(hash, Encoding.UTF8.GetBytes(name));
                Append(hash, Encoding.UTF8.GetBytes(image.GetAttribute(name) ?? ""));
            }
        }
        Append(hash, []);
        foreach (string name in image.ProfileNames.Order(StringComparer.Ordinal))
        {
            Append(hash, Encoding.UTF8.GetBytes(name));
            Append(hash, image.GetProfile(name)!.ToByteArray());
        }
        Append(hash, []);
        // ImageMagick drops unknown PNG chunks, so include them directly rather than silently ignoring them.
        if (IsPng(source))
        {
            AppendUnknownPngChunks(hash, source);
        }
        Append(hash, []);

        using var pixels = image.GetPixels();
        for (int y = 0; y < image.Height; y++)
        {
            var mapping = image.ColorSpace == ColorSpace.CMYK ? PixelMapping.CMYKA : PixelMapping.RGBA;
            var row = pixels.ToShortArray(0, y, image.Width, 1, mapping)!;
            if (!BitConverter.IsLittleEndian)
            {
                BinaryPrimitives.ReverseEndianness(row, row);
            }
            hash.AppendData(MemoryMarshal.AsBytes(row.AsSpan()));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static double[] GetAspectRatio(Density density)
    {
        // Both absent and square-pixel densities describe square pixels. Retain invalid/asymmetric densities.
        return density.X == density.Y ? [1, 1] : density.Y == 0 ? [density.X, 0] : [density.X / density.Y, 1];
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }

    private static void AppendUnknownPngChunks(IncrementalHash hash, ReadOnlySpan<byte> source)
    {
        for (int offset = 8; offset <= source.Length - 12;)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(source[offset..]);
            if (length > source.Length - offset - 12)
            {
                Append(hash, source[offset..]);
                return;
            }
            var type = source.Slice(offset + 4, 4);
            string name = Encoding.ASCII.GetString(type);
            // Pixels, explicit descriptive metadata, and properties/profiles handled above.
            if (name is not ("IHDR" or "PLTE" or "IDAT" or "IEND" or "tRNS" or
                "pHYs" or "tIME" or "tEXt" or "zTXt" or "iTXt" or "iCCP" or "eXIf" or
                "bKGD" or "cHRM" or "gAMA" or "sRGB"))
            {
                Append(hash, source.Slice(offset + 4, (int)length + 4));
            }
            offset += (int)length + 12;
            if (name == "IEND")
            {
                if (offset < source.Length)
                {
                    Append(hash, source[offset..]);
                }
                return;
            }
        }
    }
}
