using ImageMagick;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Image;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

try
{
    using var probe = new ImageDataProbe(args);
    await probe.RunAsync();
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}

sealed class ImageDataProbe : IDisposable
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    private readonly string root = Path.Combine(Path.GetTempPath(), "syncclipboard-image-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
    private readonly List<string> checks = [];
    private readonly string report;
    private readonly string expectedRid;
    private CancellationToken Token => deadline.Token;

    public ImageDataProbe(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: ImageProbe <expected-rid> <new-report.json>");
        expectedRid = args[0];
        report = Path.GetFullPath(args[1]);
        Require(!File.Exists(report), "Refusing to overwrite an existing report.");
        Directory.CreateDirectory(root);
    }

    public async Task RunAsync()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var actualRid = os + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        Require(actualRid == expectedRid, $"Expected {expectedRid}, running {actualRid}.");
        VerifyLosslessFormats();
        await VerifyClipboardCacheAsync();
        await VerifyCompatibilityFormatsAsync();
        await VerifyAnimationAsync();
        await VerifyLargeImageAsync();
        await VerifyInvalidInputAndCancellationAsync();
#if WINDOWS
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            throw new PlatformNotSupportedException("The Windows image probe requires Windows 10 or later.");
        VerifySystemDrawing();
#endif
        var nativeFiles = Directory.GetFiles(AppContext.BaseDirectory, "Magick.Native*", SearchOption.AllDirectories);
        Require(nativeFiles.Length == 1, "Expected exactly one native Magick library in the RID-specific publish output.");
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
        {
            runtimeIdentifier = actualRid,
            magickVersion = MagickNET.Version,
            imageMagickVersion = MagickNET.ImageMagickVersion,
            managedAssembly = typeof(MagickImage).Assembly.GetName().Name,
            managedSha256 = Hash(await File.ReadAllBytesAsync(typeof(MagickImage).Assembly.Location, Token)),
            coreSha256 = Hash(await File.ReadAllBytesAsync(typeof(MagickFormat).Assembly.Location, Token)),
#if WINDOWS
            systemDrawingSha256 = Hash(await File.ReadAllBytesAsync(typeof(IMagickImageExtentions).Assembly.Location, Token)),
            drawingCommonSha256 = Hash(await File.ReadAllBytesAsync(typeof(System.Drawing.Bitmap).Assembly.Location, Token)),
#endif
            nativeFile = Path.GetFileName(nativeFiles[0]),
            nativeSha256 = Hash(await File.ReadAllBytesAsync(nativeFiles[0], Token)),
            checks
        }, ReportOptions), Token);
    }

    private void VerifyLosslessFormats()
    {
        var pixels = Pattern(48, 32);
        using var image = FromPixels(pixels, 48, 32);
        foreach (var format in new[] { MagickFormat.Png, MagickFormat.Tiff })
        {
            using var decoded = new MagickImage(image.ToByteArray(format));
            VerifyPixels(decoded, pixels, 48, 32);
        }
        using var opaque = new MagickImage(MagickColors.Blue, 64, 48);
        using var bmp = new MagickImage(opaque.ToByteArray(MagickFormat.Bmp));
        VerifyBlue(bmp);
        Passed("native PNG/TIFF alpha and pixel roundtrips; BMP decoding");
    }

    private async Task VerifyClipboardCacheAsync()
    {
        var pixels = Pattern(48, 32);
        using var image = FromPixels(pixels, 48, 32);
        var clipboard = ClipboardImage.TryCreateImage(image.ToByteArray(MagickFormat.Tiff));
        Require(clipboard is not null, "Valid clipboard image was rejected.");
        var file = Path.Combine(root, "clipboard.png");
        await clipboard!.Save(file, Token);
        using (var saved = new MagickImage(file)) VerifyPixels(saved, pixels, 48, 32);
        await clipboard.Save(Path.Combine(root, "cached.png"), Token);
        Require(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "cached.png"))), "Cached file differs.");
        using (var saved = new MagickImage(await clipboard.SaveToBytes(Token))) VerifyPixels(saved, pixels, 48, 32);
        await Task.WhenAll(Enumerable.Range(1, 8).Select(async seed =>
        {
            var expected = Pattern(32, 24, seed);
            using var source = FromPixels(expected, 32, 24);
            var item = new ClipboardImage(source.ToByteArray(MagickFormat.Png));
            using var saved = new MagickImage(await item.SaveToBytes(Token));
            VerifyPixels(saved, expected, 32, 24);
        }));
        Passed("ClipboardImage save, byte conversion, repeated cache and concurrent image isolation");
    }

    private async Task VerifyCompatibilityFormatsAsync()
    {
        using var source = new MagickImage(MagickColors.Blue, 64, 48);
        var formats = new[]
        {
            ("webp", MagickFormat.WebP), ("avif", MagickFormat.Avif),
            ("heic", MagickFormat.Heic), ("heif", MagickFormat.Heic)
        };
        foreach (var (extension, format) in formats)
        {
            var path = Path.Combine(root, "single-" + extension + "." + extension);
            if (format == MagickFormat.Heic)
                File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "blue-64x48.heic"), path);
            else
                await source.WriteAsync(path, format, Token);
            Require(ImageHelper.IsComplexImage(path), "Supported complex image extension was not recognized.");
            var converted = await ImageHelper.CompatibilityCast(path, root, Token);
            Require(Path.GetExtension(converted) == ".jpg", "Single-frame compatibility output is not JPEG.");
            using var decoded = new MagickImage(converted);
            Require(decoded.Format == MagickFormat.Jpeg, "Compatibility file has incorrect encoding.");
            VerifyBlue(decoded);
        }
        Passed("WebP/AVIF/HEIC/HEIF single-frame compatibility conversion to JPEG");
    }

    private async Task VerifyAnimationAsync()
    {
        using var frames = new MagickImageCollection();
        frames.Add(new MagickImage(MagickColors.Red, 64, 48) { AnimationDelay = 10 });
        frames.Add(new MagickImage(MagickColors.Blue, 64, 48) { AnimationDelay = 20 });
        var path = Path.Combine(root, "animation.webp");
        await frames.WriteAsync(path, MagickFormat.WebP, Token);
        var converted = await ImageHelper.CompatibilityCast(path, root, Token);
        Require(Path.GetExtension(converted) == ".gif", "Multi-frame compatibility output is not GIF.");
        using var decoded = new MagickImageCollection(converted);
        Require(decoded.Count == 2, "Animation frame count changed.");
        Require(decoded[0].Width == 64 && decoded[0].Height == 48, "Animation canvas changed.");
        Require(decoded[0].AnimationDelay == 10 && decoded[1].AnimationDelay == 20, "Animation timing changed.");
        using var pixels = decoded[0].GetPixels();
        var first = pixels.ToByteArray(PixelMapping.RGB)!;
        Require(first[0] >= 250 && first[1] <= 5 && first[2] <= 5, "Animation first-frame color changed.");
        VerifyBlue(decoded[1]);
        Passed("animated WebP compatibility conversion preserves GIF frames, canvas, timing and colors");
    }

    private async Task VerifyLargeImageAsync()
    {
        var pixels = Pattern(4096, 2048);
        using var image = FromPixels(pixels, 4096, 2048);
        var clipboard = new ClipboardImage(image.ToByteArray(MagickFormat.Png));
        using var decoded = new MagickImage(await clipboard.SaveToBytes(Token));
        VerifyPixels(decoded, pixels, 4096, 2048);
        Passed("4096x2048 image preserves all 32 MiB of RGBA pixel data");
    }

    private async Task VerifyInvalidInputAndCancellationAsync()
    {
        foreach (var invalid in new byte[]?[] { null, [], [1, 2, 3, 4], [137, 80, 78, 71, 13, 10, 26, 10] })
        {
            Require(ClipboardImage.TryCreateImage(invalid) is null, "Invalid image was accepted.");
        }
        try
        {
            await new ClipboardImage([1, 2, 3]).SaveToBytes(Token);
            throw new InvalidOperationException("Invalid image conversion succeeded.");
        }
        catch (MagickException) { }
        using var source = new MagickImage(MagickColors.Blue, 64, 48);
        var clipboard = new ClipboardImage(source.ToByteArray(MagickFormat.Png));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await clipboard.SaveToBytes(canceled.Token);
            throw new InvalidOperationException("Canceled conversion succeeded.");
        }
        catch (OperationCanceledException) { }
        using var recovered = new MagickImage(await clipboard.SaveToBytes(Token));
        VerifyBlue(recovered);
        Passed("invalid images rejected, cancellation honored and subsequent valid conversion succeeds");
    }

#if WINDOWS
    [SupportedOSPlatform("windows10.0")]
    private void VerifySystemDrawing()
    {
        using var image = new MagickImage(MagickColors.Blue, 64, 48);
        using var bitmap = image.ToBitmap();
        Require(bitmap.Width == 64 && bitmap.Height == 48, "SystemDrawing bitmap dimensions changed.");
        var color = bitmap.GetPixel(20, 20);
        Require(color.A == 255 && color.R == 0 && color.G == 0 && color.B == 255, "SystemDrawing bitmap pixels changed.");
        using var converted = new MagickImage();
        converted.Read(bitmap);
        VerifyBlue(converted);
        Passed("Windows SystemDrawing bitmap conversion without UI");
    }
#endif

    private static byte[] Pattern(int width, int height, int seed = 0)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            var alpha = (byte)((i + seed) % 3 == 0 ? 0 : (i + seed) % 3 == 1 ? 128 : 255);
            pixels[i * 4] = alpha == 0 ? (byte)0 : (byte)(i + seed);
            pixels[i * 4 + 1] = alpha == 0 ? (byte)0 : (byte)(i / width + seed * 5);
            pixels[i * 4 + 2] = alpha == 0 ? (byte)0 : (byte)(i * 13 + seed);
            pixels[i * 4 + 3] = alpha;
        }
        return pixels;
    }

    private static MagickImage FromPixels(byte[] pixels, uint width, uint height)
    {
        var image = new MagickImage();
        image.ReadPixels(pixels, new PixelReadSettings(width, height, StorageType.Char, PixelMapping.RGBA));
        return image;
    }

    private static void VerifyPixels(MagickImage image, byte[] expected, uint width, uint height)
    {
        Require(image.Width == width && image.Height == height && image.HasAlpha, "Image dimensions or alpha channel changed.");
        using var pixels = image.GetPixels();
        Require(expected.SequenceEqual(pixels.ToByteArray(PixelMapping.RGBA)!), "RGBA pixels changed.");
    }

    private static void VerifyBlue(IMagickImage<ushort> image)
    {
        Require(image.Width == 64 && image.Height == 48, "Opaque image dimensions changed.");
        using var pixels = image.GetPixels();
        var rgb = pixels.ToByteArray(PixelMapping.RGB)!;
        for (var i = 0; i < rgb.Length; i += 3)
            Require(rgb[i] <= 5 && rgb[i + 1] <= 5 && rgb[i + 2] >= 250, "Opaque blue pixels changed beyond codec tolerance.");
    }

    private void Passed(string check)
    {
        checks.Add(check);
        Console.WriteLine("Image smoke pass: " + check);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        deadline.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
