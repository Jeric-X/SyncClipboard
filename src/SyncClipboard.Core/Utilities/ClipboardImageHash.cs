using ImageMagick;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities;

internal static class ClipboardImageHash
{
    // Metadata retained by the PNG encoder participates, except for these descriptive/diagnostic fields.
    private static readonly HashSet<string> IgnoredAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Title", "Author", "Description", "Copyright", "Creation Time", "Software",
        "Disclaimer", "Warning", "Source", "Comment", "date:create", "date:modify", "date:timestamp",
        "png:tIME", "png:pHYs",
        // Decoder diagnostics describe storage, or duplicate properties hashed below.
        "png:IHDR.bit-depth-orig", "png:IHDR.bit_depth", "png:IHDR.color-type-orig", "png:IHDR.color_type",
        "png:IHDR.interlace_method", "png:IHDR.width,height", "png:PLTE.number_colors", "png:text", "png:tRNS",
        "png:bKGD", "png:cHRM", "png:gAMA", "tiff:endian", "tiff:rows-per-strip", "tiff:photometric", "tiff:alpha"
    };

    internal static string Compute(MagickImage image)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendMetadata(hash, image);
        Append(hash, []);
        AppendPixels(hash, image);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static void ApplyOrientation(MagickImage image)
    {
        // ImageMagick has decoded orientation and display properties. Ignore the remaining EXIF payload,
        // and remove it before rotation makes embedded dimensions or orientation stale.
        image.RemoveProfile("exif");
        var exifAttributes = image.AttributeNames
            .Where(name => name.StartsWith("exif:", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (string name in exifAttributes)
        {
            image.RemoveAttribute(name);
        }
        bool swapsAxes = image.Orientation is >= OrientationType.LeftTop and <= OrientationType.LeftBottom;
        bool fullCanvas = image.Page.Width == image.Width && image.Page.Height == image.Height &&
            image.Page.X == 0 && image.Page.Y == 0;
        var density = image.Density;
        image.AutoOrient();
        if (fullCanvas)
        {
            // ImageMagick transpose/transverse can leave the old virtual canvas even for a whole image.
            image.ResetPage();
        }
        if (swapsAxes)
        {
            image.Density = new Density(density.Y, density.X, density.Units);
        }
    }

    internal static void PreparePngMetadata(MagickImage image)
    {
        var attributes = image.AttributeNames.Order(StringComparer.Ordinal)
            .Where(name => !IgnoredAttributes.Contains(name) && name != "png:sRGB")
            .Select(name => (Name: name, Value: image.GetAttribute(name)!)).ToArray();
        foreach (string name in image.AttributeNames.ToArray())
        {
            image.RemoveAttribute(name);
        }
        foreach (var (name, value) in attributes)
        {
            image.SetAttribute(name, value);
        }

        var density = image.Density;
        if (density.X == density.Y)
        {
            // Fixed square-pixel resolution, regardless of missing/zero/absolute source DPI.
            image.Density = new Density(96);
        }
        else
        {
            NormalizePixelAspectRatio(image, density);
        }
        if (image.Density.Units == DensityUnit.PixelsPerCentimeter)
        {
            // Avoid truncation of decoded PNG pixels-per-meter values on re-encoding.
            image.Density = new Density(Math.BitIncrement(image.Density.X), Math.BitIncrement(image.Density.Y),
                DensityUnit.PixelsPerCentimeter);
        }
    }

    private static void NormalizePixelAspectRatio(MagickImage image, Density density)
    {
        double scale = density.Units switch
        {
            DensityUnit.PixelsPerCentimeter => 100,
            DensityUnit.PixelsPerInch => 100 / 2.54,
            _ => 1
        };
        double x = Math.Round(density.X * scale);
        double y = Math.Round(density.Y * scale);
        if (x is < 1 or > uint.MaxValue || y is < 1 or > uint.MaxValue ||
            !double.IsFinite(x) || !double.IsFinite(y) || x / y != density.X / density.Y)
        {
            return; // Do not approximate an aspect ratio that cannot be represented exactly this way.
        }
        uint a = (uint)x;
        uint b = (uint)y;
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        image.Density = new Density(x / a, y / a, DensityUnit.Undefined);
    }

    private static void AppendMetadata(IncrementalHash hash, MagickImage image)
    {
        // Hash the actual PNG output, without further color conversion or pixel quantization.
        Append(hash, JsonSerializer.SerializeToUtf8Bytes(new
        {
            image.Width,
            image.Height,
            image.ColorSpace,
            image.Gamma,
            Chromaticity = new[]
            {
                NormalizePrimary(image.Chromaticity.Red), NormalizePrimary(image.Chromaticity.Green),
                NormalizePrimary(image.Chromaticity.Blue), NormalizePrimary(image.Chromaticity.White)
            },
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
            if (!IgnoredAttributes.Contains(name) && name != "png:sRGB")
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
    }

    // PNG cHRM has five decimal places; keep those distinctions without the decoder's floating-point tails.
    private static double[] NormalizePrimary(IPrimaryInfo primary) =>
        [Math.Round(primary.X, 8), Math.Round(primary.Y, 8), Math.Round(primary.Z, 8)];

    private static void AppendPixels(IncrementalHash hash, MagickImage image)
    {
        using var pixels = image.GetPixels();
        for (int y = 0; y < image.Height; y++)
        {
            var row = pixels.ToShortArray(0, y, image.Width, 1, PixelMapping.RGBA)!;
            if (!BitConverter.IsLittleEndian)
            {
                BinaryPrimitives.ReverseEndianness(row, row);
            }
            hash.AppendData(MemoryMarshal.AsBytes(row.AsSpan()));
        }
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
}
