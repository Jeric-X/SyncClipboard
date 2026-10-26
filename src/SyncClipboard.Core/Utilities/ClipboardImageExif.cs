using ImageMagick;
using System.Buffers.Binary;

namespace SyncClipboard.Core.Utilities;

internal static class ClipboardImageExif
{
    private static readonly string[] RedundantAttributes =
    [
        "exif:ExifOffset", "exif:Orientation", "exif:ColorSpace", "exif:PixelXDimension", "exif:PixelYDimension",
        "exif:ImageDescription", "exif:Software", "exif:DateTime", "exif:Artist", "exif:Copyright",
        "exif:DateTimeOriginal", "exif:DateTimeDigitized"
    ];

    internal static void RemoveRedundantProfile(MagickImage image)
    {
        if (!IsRedundant(image))
        {
            return;
        }
        image.RemoveProfile("exif");
        foreach (string name in RedundantAttributes)
        {
            image.RemoveAttribute(name);
        }
    }

    // Only omit a completely understood profile. ExifProfile.Values alone can silently skip malformed tags.
    // Any other tag, thumbnail or unsupported structure keeps the original profile in the fingerprint.
    private static bool IsRedundant(MagickImage image)
    {
        var profile = image.GetProfile("exif");
        if (profile is null)
        {
            return false;
        }
        ReadOnlySpan<byte> data = profile.ToByteArray();
        if (data.StartsWith("Exif\0\0"u8))
        {
            data = data[6..];
        }
        if (data.Length < 8 || !(data[..2].SequenceEqual("II"u8) || data[..2].SequenceEqual("MM"u8)))
        {
            return false;
        }
        bool littleEndian = data[0] == 'I';
        return Read16(data[2..], littleEndian) == 42 &&
            IsRedundantDirectory(data, Read32(data[4..], littleEndian), littleEndian, image, true);
    }

    private static bool IsRedundantDirectory(ReadOnlySpan<byte> data, uint offset, bool littleEndian,
        MagickImage image, bool allowSubdirectory)
    {
        if (offset < 8 || offset > data.Length - 2)
        {
            return false;
        }
        int start = (int)offset;
        int count = Read16(data[start..], littleEndian);
        int end = start + 2 + (count * 12);
        if (end > data.Length - 4 || Read32(data[end..], littleEndian) != 0)
        {
            return false;
        }
        var tags = new HashSet<ushort>();
        for (int i = start + 2; i < end; i += 12)
        {
            var entry = data.Slice(i, 12);
            ushort tag = Read16(entry, littleEndian);
            if (!tags.Add(tag) || !IsRedundantEntry(data, entry, littleEndian, image, allowSubdirectory))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsRedundantEntry(ReadOnlySpan<byte> data, ReadOnlySpan<byte> entry, bool littleEndian,
        MagickImage image, bool allowSubdirectory)
    {
        ushort tag = Read16(entry, littleEndian);
        ushort type = Read16(entry[2..], littleEndian);
        uint count = Read32(entry[4..], littleEndian);
        uint value = type == 3 ? Read16(entry[8..], littleEndian) : Read32(entry[8..], littleEndian);
        if (tag is 0x010E or 0x0131 or 0x0132 or 0x013B or 0x8298 or 0x9003 or 0x9004)
        {
            // Explicit descriptive ASCII fields; validate their storage before ignoring them.
            return type == 2 && IsValidText(data, entry, count, value);
        }
        if (count != 1)
        {
            return false;
        }
        if (tag == 0x8769) // EXIF subdirectory pointer is storage, not image content.
        {
            return type == 4 && allowSubdirectory && IsRedundantDirectory(data, value, littleEndian, image, false);
        }
        return IsRedundantScalar(tag, type, value, image);
    }

    private static bool IsValidText(ReadOnlySpan<byte> data, ReadOnlySpan<byte> entry, uint count, uint value)
    {
        uint offset = count <= 4 ? 0 : value;
        var storage = count <= 4 ? entry[8..] : data;
        return count > 0 && offset <= storage.Length && count <= storage.Length - offset &&
            storage[(int)(offset + count - 1)] == 0;
    }

    private static bool IsRedundantScalar(ushort tag, ushort type, uint value, MagickImage image) =>
        tag switch
        {
            // Orientation, decoded dimensions and the standard EXIF sRGB declaration.
            0x0112 => type == 3 && value is >= 1 and <= 8 &&
                value == (uint)(image.Orientation == OrientationType.Undefined ? OrientationType.TopLeft : image.Orientation),
            0xA002 => type is 3 or 4 && value == image.Width,
            0xA003 => type is 3 or 4 && value == image.Height,
            0xA001 => type == 3 && value == 1 && image.ColorSpace == ColorSpace.sRGB,
            _ => false
        };

    private static ushort Read16(ReadOnlySpan<byte> data, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);

    private static uint Read32(ReadOnlySpan<byte> data, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt32BigEndian(data);
}
