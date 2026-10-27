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
    [DataRow(OrientationType.TopRight)]
    [DataRow(OrientationType.BottomRight)]
    [DataRow(OrientationType.BottomLeft)]
    [DataRow(OrientationType.LeftTop)]
    [DataRow(OrientationType.RightTop)]
    [DataRow(OrientationType.RightBottom)]
    [DataRow(OrientationType.LeftBottom)]
    public async Task OrientationAndMirroringMatchBakedPixels(OrientationType orientation)
    {
        using var source = CreateImage();
        source.Orientation = orientation;
        source.Density = new Density(100, 200);
        var tagged = source.ToByteArray(MagickFormat.Png);
        source.AutoOrient();
        if (orientation is >= OrientationType.LeftTop and <= OrientationType.LeftBottom)
        {
            source.Density = new Density(200, 100);
        }
        AssertSameImage(tagged, source.ToByteArray(MagickFormat.Png));
        var clipboardImage = new ClipboardImage(tagged);
        var saved = await clipboardImage.SaveToBytes(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(clipboardImage, new ClipboardImage(saved));
    }

    [TestMethod]
    [DataRow(OrientationType.RightTop)]
    [DataRow(OrientationType.RightBottom)]
    public async Task ExifOrientationMatchesBakedPixels(OrientationType orientation)
    {
        using var source = CreateImage();
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Orientation, (ushort)orientation);
        profile.SetValue(ExifTag.Model, "Camera metadata does not affect decoded pixels");
        profile.SetValue(ExifTag.PixelXDimension, new Number(source.Width));
        profile.SetValue(ExifTag.PixelYDimension, new Number(source.Height));
        source.SetProfile(profile);
        source.Orientation = orientation;
        var tagged = source.ToByteArray(MagickFormat.Png);
        source.RemoveProfile("exif");
        source.AutoOrient();
        var baked = source.ToByteArray(MagickFormat.Png);
        AssertSameImage(tagged, baked);
        var image = new ClipboardImage(tagged);
        var saved = await image.SaveToBytes(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(image, new ClipboardImage(saved));
        var cached = await new ClipboardImage(baked).SaveToBytes(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(image, new ClipboardImage(cached));
    }

    [TestMethod]
    public void EquivalentSrgbAndExifMetadataMatch()
    {
        // Reproduce the user's two PNGs without committing their private screenshot:
        // sRGB + EXIF dimensions versus cHRM + orNT + DPI + TIFF encoder attributes.
        using var source = CreateImage();
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.ColorSpace, (ushort)1);
        profile.SetValue(ExifTag.PixelXDimension, new Number(source.Width));
        profile.SetValue(ExifTag.PixelYDimension, new Number(source.Height));
        source.SetProfile(profile);
        source.Settings.SetDefine(MagickFormat.Png, "exclude-chunk", "sRGB,gAMA");
        var original = InsertPngChunk(source.ToByteArray(MagickFormat.Png), "sRGB", [0]);
        source.RemoveProfile("exif");
        source.Orientation = OrientationType.TopLeft;
        source.Density = new Density(72);
        source.SetAttribute("tiff:alpha", "associated");
        source.SetAttribute("date:modify", "2026-10-26T09:13:29+00:00");
        AssertSameImage(original, source.ToByteArray(MagickFormat.Png));
    }

    [TestMethod]
    public void DifferentColorPrimariesRemainDifferent()
    {
        using var source = CreateImage();
        source.Settings.SetDefine(MagickFormat.Png, "exclude-chunk", "sRGB");
        var original = source.ToByteArray(MagickFormat.Png);
        var chromaticity = source.Chromaticity;
        source.Chromaticity = new ChromaticityInfo(new PrimaryInfo(0.63, 0.33, 0.04),
            chromaticity.Green, chromaticity.Blue, chromaticity.White);
        Assert.AreNotEqual(new ClipboardImage(original), new ClipboardImage(source.ToByteArray(MagickFormat.Png)));
    }

    [TestMethod]
    public void BigEndianExifWithLongDimensionsMatchesPixels()
    {
        using var source = CreateImage();
        var original = source.ToByteArray(MagickFormat.Png);
        // Synthetic equivalent of the user's EXIF layout: big endian, sRGB, LONG width/height.
        var exif = Convert.FromHexString(
            "4D4D002A00000008000187690004000000010000001A000000000003" +
            "A00100030000000100010000A00200040000000100000003A0030004000000010000000200000000");

        AssertSameImage(original, InsertPngChunk(original, "eXIf", exif));
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
    public async Task SrgbIccIsPreservedWhenApplyingOrientation()
    {
        using var source = CreateImage();
        source.SetProfile(ColorProfiles.SRGB);
        source.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
        source.Orientation = OrientationType.RightTop;
        var image = new ClipboardImage(source.ToByteArray(MagickFormat.Png));
        var saved = await image.SaveToBytes(TestContext.CancellationTokenSource.Token);
        using var decoded = new MagickImage();
        decoded.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
        decoded.Read(saved);
        CollectionAssert.AreEqual(ColorProfiles.SRGB.ToByteArray(), decoded.GetColorProfile()!.ToByteArray());
        Assert.AreEqual(image, new ClipboardImage(saved));
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
    public void AnimatedPngOrientationIsNotComparedUsingOnlyFirstFrame()
    {
        // Synthetic 3x2 APNG: solid red first frame, blue upper-left pixel in the second frame.
        // A 180-degree turn leaves frame 1 identical but changes frame 2.
        var original = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAYAAACddGYaAAAACGFjVEwAAAACAAAAAPONk3AAAAAaZmNU" +
            "TAAAAAAAAAADAAAAAgAAAAAAAAAAAAEACgAAP7ZcWAAAABRJREFUeJxj/M/A8J8BCphgDBANAC8fAgK0" +
            "KqlqAAAAGmZjVEwAAAABAAAAAQAAAAEAAAAAAAAAAAABAAoAAMEM2gQAAAARZmRBVAAAAAJ4nGNgYPj/" +
            "HwADAgH/9Xul1wAAAABJRU5ErkJggg==");
        var rotated = InsertPngChunk(original, "orNT", [(byte)OrientationType.BottomRight]);
        Assert.AreNotEqual(new ClipboardImage(original), new ClipboardImage(rotated));
    }

    [TestMethod]
    public async Task IdenticalSourceBytes_ReuseImageAcrossConcurrentReads()
    {
        using var source = CreateImage();
        var bytes = source.ToByteArray(MagickFormat.Png);
        var token = TestContext.CancellationTokenSource.Token;
        var images = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => ClipboardImage.TryCreateImageAsync((byte[])bytes.Clone(), token)));

        Assert.IsNotNull(images[0]);
        foreach (var image in images)
        {
            Assert.AreSame(images[0], image);
        }
        Assert.AreSame(images[0], ClipboardImage.TryCreateImage(bytes));
    }

    [TestMethod]
    public void DifferentSourceBytes_DoNotReuseStaleImage()
    {
        using var source = CreateImage();
        var bytes = source.ToByteArray(MagickFormat.Bmp3);
        var changedBytes = (byte[])bytes.Clone();
        var original = ClipboardImage.TryCreateImage(bytes);
        Assert.IsNotNull(original);
        // Change one pixel in a separate buffer before transferring ownership, retaining the same length.
        int pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(changedBytes.AsSpan(10));
        changedBytes[pixelOffset] ^= 1;
        var changed = ClipboardImage.TryCreateImage(changedBytes);

        Assert.IsNotNull(changed);
        Assert.AreNotSame(original, changed);
        Assert.AreNotEqual(original.ContentHash, changed.ContentHash);
        Assert.AreSame(changed, ClipboardImage.TryCreateImage((byte[])changedBytes.Clone()));
    }

    [TestMethod]
    public void InvalidSource_DoesNotReplaceCachedImage()
    {
        using var source = CreateImage();
        var bytes = source.ToByteArray(MagickFormat.Png);
        var snapshot = (byte[])bytes.Clone();
        var original = ClipboardImage.TryCreateImage(bytes);
        Assert.IsNotNull(original);
        Assert.IsNull(ClipboardImage.TryCreateImage(new byte[bytes.Length]));
        Assert.IsNull(ClipboardImage.TryCreateImage(null));
        Assert.AreSame(original, ClipboardImage.TryCreateImage(snapshot));
        Assert.AreEqual(new ClipboardImage(snapshot).ContentHash, original.ContentHash);
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
