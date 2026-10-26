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
        // Keep attributes identical too: encoders otherwise add rounded chromaticities or a palette background.
        var zero = new PrimaryInfo(0, 0, 0);
        source.Chromaticity = new ChromaticityInfo(zero, zero, zero, zero);
        source.Settings.SetDefine(MagickFormat.Png, "exclude-chunk", "bKGD,cHRM");
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

        Assert.AreNotEqual(first.ContentHash, second.ContentHash);
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
    public async Task OrientationMetadataParticipatesAndIsAppliedWhenSaving()
    {
        using var source = CreateImage();
        var plain = new ClipboardImage(source.ToByteArray(MagickFormat.Png));
        source.Orientation = OrientationType.RightTop;
        var bytes = source.ToByteArray(MagickFormat.Png);
        using var decoded = new MagickImage(bytes);
        Assert.AreEqual(OrientationType.RightTop, decoded.Orientation);
        var rotated = new ClipboardImage(bytes);
        Assert.AreNotEqual(plain, rotated);
        decoded.AutoOrient();
        using var saved = new MagickImage(await rotated.SaveToBytes(TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(decoded.Width, saved.Width);
        Assert.AreEqual(decoded.Height, saved.Height);
        using var expectedPixels = decoded.GetPixels();
        using var actualPixels = saved.GetPixels();
        CollectionAssert.AreEqual(expectedPixels.ToShortArray(PixelMapping.RGBA), actualPixels.ToShortArray(PixelMapping.RGBA));
    }

    [TestMethod]
    public async Task ColorProfileParticipatesAndIsPreserved()
    {
        using var source = CreateImage();
        var plain = new ClipboardImage(source.ToByteArray(MagickFormat.Png));
        source.SetProfile(ColorProfiles.AdobeRGB1998);
        var bytes = source.ToByteArray(MagickFormat.Png);
        var tagged = new ClipboardImage(bytes);
        Assert.AreNotEqual(plain, tagged);
        var saved = await tagged.SaveToBytes(TestContext.CancellationTokenSource.Token);
        using var decoded = new MagickImage(saved);
        CollectionAssert.AreEqual(ColorProfiles.AdobeRGB1998.ToByteArray(), decoded.GetColorProfile()!.ToByteArray());
        Assert.AreEqual(tagged, new ClipboardImage(saved));
    }

    [TestMethod]
    public void ExplicitSrgbIccIsNotDiscardedDuringComparison()
    {
        using var source = CreateImage();
        var plain = source.ToByteArray(MagickFormat.Png);
        source.SetProfile(ColorProfiles.SRGB);
        source.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
        var tagged = source.ToByteArray(MagickFormat.Png);
        Assert.AreNotEqual(new ClipboardImage(plain), new ClipboardImage(tagged));
    }

    [TestMethod]
    public async Task CmykIccIsAppliedWhenSavingToPng()
    {
        using var source = new MagickImage();
        source.ReadPixels([30, 80, 120, 40], new PixelReadSettings(1, 1, StorageType.Char, PixelMapping.CMYK));
        source.SetProfile(ColorProfiles.USWebCoatedSWOP);
        var bytes = source.ToByteArray(MagickFormat.Tiff);
        using var expected = new MagickImage(bytes);
        Assert.IsTrue(expected.TransformColorSpace(ColorProfiles.SRGB));
        var clipboardImage = new ClipboardImage(bytes);
        using var actual = new MagickImage(await clipboardImage.SaveToBytes(TestContext.CancellationTokenSource.Token));
        using var expectedPixels = expected.GetPixels();
        using var actualPixels = actual.GetPixels();
        CollectionAssert.AreEqual(expectedPixels.ToShortArray(PixelMapping.RGBA), actualPixels.ToShortArray(PixelMapping.RGBA));
    }

    [TestMethod]
    public async Task GammaMetadataParticipatesAndDoesNotChangeSavedPixels()
    {
        using var source = CreateImage();
        var original = source.ToByteArray(MagickFormat.Png);
        var bytes = WithPngGamma(original, 70000);
        var custom = new ClipboardImage(bytes);
        Assert.AreNotEqual(new ClipboardImage(original), custom);
        var saved = await custom.SaveToBytes(TestContext.CancellationTokenSource.Token);
        using var decoded = new MagickImage(saved);
        Assert.AreEqual(0.7, decoded.Gamma, 0.00001);
        using var beforePixels = source.GetPixels();
        using var afterPixels = decoded.GetPixels();
        CollectionAssert.AreEqual(beforePixels.ToShortArray(PixelMapping.RGBA), afterPixels.ToShortArray(PixelMapping.RGBA));
        Assert.AreEqual(custom, new ClipboardImage(saved));
    }

    [TestMethod]
    [DataRow("Comment")]
    [DataRow("Author")]
    [DataRow("Software")]
    [DataRow("Creation Time")]
    public void AllowlistedTextDoesNotChangeIdentity(string keyword)
    {
        using var source = CreateImage();
        var original = source.ToByteArray(MagickFormat.Png);
        source.SetAttribute(keyword, "descriptive metadata");
        AssertSameImage(original, source.ToByteArray(MagickFormat.Png));
    }

    [TestMethod]
    [DataRow("PrivateSetting")]
    [DataRow("Comment:PrivateSetting")]
    [DataRow("XML:com.adobe.xmp")]
    public void UnlistedTextParticipatesInComparison(string keyword)
    {
        using var source = CreateImage();
        var original = source.ToByteArray(MagickFormat.Png);
        source.SetAttribute(keyword, "custom metadata");
        var changed = source.ToByteArray(MagickFormat.Png);
        Assert.AreNotEqual(new ClipboardImage(original), new ClipboardImage(changed));
    }

    [TestMethod]
    public void PixelAspectRatioParticipatesInComparison()
    {
        using var source = CreateImage();
        source.Density = new Density(100, 200);
        var original = source.ToByteArray(MagickFormat.Png);
        source.Density = new Density(200, 400);
        AssertSameImage(original, source.ToByteArray(MagickFormat.Png));
        source.Density = new Density(200, 200);
        Assert.AreNotEqual(new ClipboardImage(original), new ClipboardImage(source.ToByteArray(MagickFormat.Png)));
    }

    [TestMethod]
    public void UnknownPngChunkParticipatesInComparison()
    {
        using var source = CreateImage();
        var original = source.ToByteArray(MagickFormat.Png);
        var changed = InsertPngChunk(original, "scPr", [1, 2, 3]);
        Assert.AreNotEqual(new ClipboardImage(original), new ClipboardImage(changed));
    }

    [TestMethod]
    public void LosslessPngRecompressionDoesNotChangeIdentity()
    {
        using var source = CreateImage();
        source.Settings.SetDefine(MagickFormat.Png, "compression-level", 0);
        var original = source.ToByteArray(MagickFormat.Png);
        source.Settings.SetDefine(MagickFormat.Png, "compression-level", 9);
        AssertSameImage(original, source.ToByteArray(MagickFormat.Png));
    }

    [TestMethod]
    public void DifferentLaterAnimationFramesAreDifferent()
    {
        using var frames = new MagickImageCollection();
        frames.Add(new MagickImage(MagickColors.Red, 3, 2));
        frames.Add(new MagickImage(MagickColors.Blue, 3, 2));
        var original = frames.ToByteArray(MagickFormat.Gif);
        frames[1].Colorize(MagickColors.Green, new Percentage(100));
        var changed = frames.ToByteArray(MagickFormat.Gif);
        Assert.AreNotEqual(new ClipboardImage(original), new ClipboardImage(changed));
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
        Assert.AreEqual(first.ContentHash, second.ContentHash);
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

    private static byte[] WithPngGamma(byte[] png, uint gamma)
    {
        using var output = new MemoryStream();
        output.Write(png.AsSpan(0, 8));
        for (int offset = 8; offset < png.Length;)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset)) + 12);
            var type = png.AsSpan(offset + 4, 4);
            if (!type.SequenceEqual("gAMA"u8) && !type.SequenceEqual("sRGB"u8))
            {
                output.Write(png.AsSpan(offset, length));
            }
            offset += length;
        }
        var value = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(value, gamma);
        return InsertPngChunk(output.ToArray(), "gAMA", value);
    }

    private static byte[] InsertPngChunk(byte[] png, string type, byte[] value)
    {
        using var output = new MemoryStream();
        output.Write(png.AsSpan(0, 33)); // Signature and IHDR.
        var chunk = new byte[value.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)value.Length);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        value.CopyTo(chunk, 8);
        uint crc = uint.MaxValue;
        foreach (var b in chunk.AsSpan(4, value.Length + 4))
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
            }
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(value.Length + 8), ~crc);
        output.Write(chunk);
        output.Write(png.AsSpan(33));
        return output.ToArray();
    }
}
