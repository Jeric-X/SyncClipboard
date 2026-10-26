using ImageMagick;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using System.Buffers.Binary;

namespace SyncClipboard.Test;

[TestClass]
public class ClipboardImageTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void BitmapDpiChange_DoesNotChangeClipboardContent()
    {
        using var source = CreateImage();
        var before = source.ToByteArray(MagickFormat.Bmp3);
        var after = (byte[])before.Clone();
        // BITMAPINFOHEADER pixels-per-meter fields: Win+Shift+S loses these during OLE flush (#417).
        BinaryPrimitives.WriteInt32LittleEndian(before.AsSpan(38), 3780);
        BinaryPrimitives.WriteInt32LittleEndian(before.AsSpan(42), 3780);
        BinaryPrimitives.WriteInt32LittleEndian(after.AsSpan(38), 0);
        BinaryPrimitives.WriteInt32LittleEndian(after.AsSpan(42), 0);

        AssertSameImage(before, after);
    }

    [TestMethod]
    public void PngMetadataChange_DoesNotChangeClipboardContent()
    {
        using var source = CreateImage();
        var original = source.ToByteArray(MagickFormat.Png);
        source.Density = new Density(300);
        source.SetAttribute("comment", "different metadata");
        AssertSameImage(original, source.ToByteArray(MagickFormat.Png));
    }

    [TestMethod]
    [DataRow(MagickFormat.Bmp3)]
    [DataRow(MagickFormat.Tiff)]
    [DataRow(MagickFormat.WebP)]
    public void LosslessEncodingChange_DoesNotChangeClipboardContent(MagickFormat format)
    {
        using var source = CreateImage();
        var original = source.ToByteArray(MagickFormat.Png);
        source.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
        AssertSameImage(original, source.ToByteArray(format));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    public void SingleQuantumColorOrAlphaChange_IsDifferent(int channel)
    {
        using var original = CreateImage();
        using var changed = CreateImage();
        using (var pixels = changed.GetPixels())
        {
            var pixel = pixels.GetPixel(0, 0).ToArray();
            pixel[channel]--;
            pixels.SetPixel(0, 0, pixel);
        }
        var first = new ClipboardImage(original.ToByteArray(MagickFormat.Png));
        var second = new ClipboardImage(changed.ToByteArray(MagickFormat.Png));

        Assert.AreNotEqual(first.PixelHash, second.PixelHash);
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void DifferentDimensionsWithSamePixelSequence_AreDifferent()
    {
        using var wide = new MagickImage(MagickColors.Red, 6, 2);
        using var tall = new MagickImage(MagickColors.Red, 2, 6);
        var first = new ClipboardImage(wide.ToByteArray(MagickFormat.Png));
        var second = new ClipboardImage(tall.ToByteArray(MagickFormat.Png));
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public async Task OrientationIsAppliedToComparisonAndSavedPixels()
    {
        using var tagged = CreateImage();
        tagged.Orientation = OrientationType.RightTop;
        var taggedBytes = tagged.ToByteArray(MagickFormat.Tiff);
        using var decoded = new MagickImage(taggedBytes);
        Assert.AreEqual(OrientationType.RightTop, decoded.Orientation);
        decoded.AutoOrient();
        var expected = new ClipboardImage(decoded.ToByteArray(MagickFormat.Png));
        var actual = new ClipboardImage(taggedBytes);

        Assert.AreEqual(expected, actual);
        var saved = await actual.SaveToBytes(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(expected, new ClipboardImage(saved));
    }

    [TestMethod]
    public async Task ColorProfileIsAppliedToComparisonAndSavedPixels()
    {
        using var source = CreateImage();
        source.SetProfile(ColorProfiles.AdobeRGB1998);
        var taggedBytes = source.ToByteArray(MagickFormat.Png);
        using var decoded = new MagickImage(taggedBytes);
        Assert.IsTrue(decoded.TransformColorSpace(ColorProfiles.SRGB));
        decoded.Depth = decoded.DetermineBitDepth();
        var expected = new ClipboardImage(decoded.ToByteArray(MagickFormat.Png));
        var actual = new ClipboardImage(taggedBytes);
        using var untagged = CreateImage();

        Assert.AreEqual(expected, actual);
        Assert.AreNotEqual(new ClipboardImage(untagged.ToByteArray(MagickFormat.Png)), actual);
        var saved = await actual.SaveToBytes(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(expected, new ClipboardImage(saved));
    }

    [TestMethod]
    public async Task EquivalentImages_SaveSamePixelsAndProtectCachedBytes()
    {
        using var source = CreateImage();
        var input = source.ToByteArray(MagickFormat.Png);
        var original = new ClipboardImage(input);
        source.Density = new Density(300);
        var equivalent = new ClipboardImage(source.ToByteArray(MagickFormat.Png));
        Array.Clear(input);
        var token = TestContext.CancellationTokenSource.Token;
        var results = await Task.WhenAll(original.SaveToBytes(token), equivalent.SaveToBytes(token));
        CollectionAssert.AreEqual(results[0], results[1]);
        Array.Clear(results[0]);

        var directory = Directory.CreateTempSubdirectory("ClipboardImage-");
        try
        {
            var path = Path.Combine(directory.FullName, "image.png");
            await equivalent.Save(path, token);
            Assert.AreEqual(original, new ClipboardImage(await File.ReadAllBytesAsync(path, token)));
            CollectionAssert.AreEqual(results[1], await original.SaveToBytes(token));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void InvalidImage_IsRejected()
    {
        Assert.IsNull(ClipboardImage.TryCreateImage(null));
        Assert.IsNull(ClipboardImage.TryCreateImage([1, 2, 3]));
    }

    private static void AssertSameImage(byte[] firstBytes, byte[] secondBytes)
    {
        CollectionAssert.AreNotEqual(firstBytes, secondBytes);
        var first = new ClipboardImage(firstBytes);
        var second = new ClipboardImage(secondBytes);
        Assert.AreEqual(first.PixelHash, second.PixelHash);
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreEqual(new ClipboardMetaInfomation { Image = first }, new ClipboardMetaInfomation { Image = second });
    }

    private static MagickImage CreateImage()
    {
        // Include asymmetric geometry, several colors, and an opaque alpha channel at Q16 precision.
        var image = new MagickImage();
        byte[] pixels =
        [
            255, 100, 30, 255, 20, 150, 220, 255, 40, 50, 60, 255,
            30, 210, 80, 255, 180, 40, 90, 255, 70, 80, 250, 255
        ];
        image.ReadPixels(pixels, new PixelReadSettings(3, 2, StorageType.Char, PixelMapping.RGBA));
        return image;
    }
}
