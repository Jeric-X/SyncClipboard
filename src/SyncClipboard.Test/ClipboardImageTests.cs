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
        changed.Depth = 16;
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
    public async Task GammaChangesRemainDistinctAndSurviveSaving()
    {
        using var source = CreateImage();
        using (var sourcePixels = source.GetPixels())
        {
            sourcePixels.SetPixel(0, 0, [ushort.MaxValue, 100 * 257, 30 * 257, 128 * 257]);
        }
        var bytes = source.ToByteArray(MagickFormat.Png);
        var standard = new ClipboardImage(bytes);
        var custom = new ClipboardImage(WithPngGamma(bytes, 70000));

        Assert.AreNotEqual(standard, custom);
        var saved = await custom.SaveToBytes(TestContext.CancellationTokenSource.Token);
        using var decoded = new MagickImage(saved);
        Assert.AreEqual(1 / 2.2, decoded.Gamma, 0.00001);
        using var pixels = decoded.GetPixels();
        Assert.IsGreaterThan((ushort)(100 * 257), pixels.GetPixel(0, 0).ToColor().G);
        Assert.AreEqual((ushort)(128 * 257), pixels.GetPixel(0, 0).ToColor().A);
        Assert.AreEqual(custom, new ClipboardImage(saved));
    }

    [TestMethod]
    public async Task CustomGammaGrayscaleAndRgbEncodingsHaveSamePixels()
    {
        using var source = new MagickImage(new MagickColor(25700, 25700, 25700), 3, 2);
        source.Settings.SetDefine(MagickFormat.Png, "color-type", 2);
        var rgb = new ClipboardImage(WithPngGamma(source.ToByteArray(MagickFormat.Png), 70000));
        source.Settings.SetDefine(MagickFormat.Png, "color-type", 0);
        var grayscaleBytes = WithPngGamma(source.ToByteArray(MagickFormat.Png), 70000);
        using var decoded = new MagickImage(grayscaleBytes);
        Assert.AreEqual(ColorSpace.Gray, decoded.ColorSpace);
        var grayscale = new ClipboardImage(grayscaleBytes);

        Assert.AreEqual(rgb, grayscale);
        var saved = await grayscale.SaveToBytes(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(rgb, new ClipboardImage(saved));
    }

    [TestMethod]
    public async Task ExplicitSrgbOverridesConflictingGamma()
    {
        using var source = CreateImage();
        var bytes = source.ToByteArray(MagickFormat.Png);
        var taggedBytes = WithPngGamma(bytes, 70000, declareSrgb: true);
        using var decoded = new MagickImage(taggedBytes);
        Assert.IsNotNull(decoded.GetAttribute("png:sRGB"));
        var expected = new ClipboardImage(bytes);
        var actual = new ClipboardImage(taggedBytes);

        Assert.AreEqual(expected, actual);
        var saved = await actual.SaveToBytes(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(expected, new ClipboardImage(saved));
    }

    [TestMethod]
    public async Task BackgroundCreationValidatesImagesAndHonorsCancellation()
    {
        using var source = CreateImage();
        var bytes = source.ToByteArray(MagickFormat.Png);
        var token = TestContext.CancellationTokenSource.Token;
        Assert.AreEqual(new ClipboardImage(bytes), await ClipboardImage.TryCreateImageAsync(bytes, token));
        Assert.IsNull(await ClipboardImage.TryCreateImageAsync([1, 2, 3], token));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => ClipboardImage.TryCreateImageAsync(bytes, canceled.Token));
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

    private static byte[] WithPngGamma(byte[] png, uint gamma, bool declareSrgb = false)
    {
        using var output = new MemoryStream();
        output.Write(png.AsSpan(0, 33)); // PNG signature and IHDR chunk.
        Span<byte> chunk = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, 4);
        "gAMA"u8.CopyTo(chunk[4..]);
        BinaryPrimitives.WriteUInt32BigEndian(chunk[8..], gamma);
        uint crc = uint.MaxValue;
        foreach (var value in chunk[4..12])
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
            }
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk[12..], ~crc);
        output.Write(chunk);
        if (declareSrgb)
        {
            // An sRGB chunk declaring perceptual rendering intent, including its CRC.
            output.Write(Convert.FromHexString("000000017352474200AECE1CE9"));
        }

        for (int offset = 33; offset < png.Length;)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset)) + 12);
            var type = png.AsSpan(offset + 4, 4);
            if (!type.SequenceEqual("gAMA"u8) && !type.SequenceEqual("sRGB"u8))
            {
                output.Write(png.AsSpan(offset, length));
            }
            offset += length;
        }
        return output.ToArray();
    }
}
