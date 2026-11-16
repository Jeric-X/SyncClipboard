using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using ImageMagick;
using SkiaSharp;
using SyncClipboard.Desktop.Controls;
using SyncClipboard.Desktop.Utilities;

namespace SyncClipboard.Test.Desktop.History;

[TestClass]
[DoNotParallelize]
public class PreviewImageCacheTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task SharedPreviewsReuseImagesAndReleaseNativePixelsAfterEviction()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryListImageLoadingTests));
        await session.Dispatch(async () =>
        {
            var path = Path.GetTempFileName();
            WriteImage(path, 100, 50);
            using var cache = PreviewImageProvider.CreateCache(null);
            cache.SetCapacity(1024 * 1024);
            var first = new PreviewImage(cache) { FilePath = path };
            var second = new PreviewImage(cache) { FilePath = path };
            var panel = new StackPanel { Children = { first, second } };
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                await Task.WhenAll(first.PendingLoad, second.PendingLoad);
                var bitmap = first.Source!.Frames.Single();
                Assert.AreSame(bitmap, second.Source!.Frames.Single());
                first.IsVisible = false;
                await first.PendingRelease;
                Assert.IsNull(first.Source);
                Assert.AreEqual(new PixelSize(100, 50), bitmap.PixelSize);
                first.IsVisible = true;
                await first.PendingLoad;
                Assert.AreSame(bitmap, first.Source!.Frames.Single());

                panel.Children.Clear();
                await Task.WhenAll(first.PendingRelease, second.PendingRelease);
                Assert.IsNull(first.Source);
                Assert.IsNull(second.Source);
                Assert.AreEqual(new PixelSize(100, 50), bitmap.PixelSize);
                panel.Children.Add(first);
                await first.PendingLoad;
                Assert.AreSame(bitmap, first.Source!.Frames.Single());
                cache.SetCapacity(0);
                Assert.AreEqual(new PixelSize(100, 50), bitmap.PixelSize);
                first.IsVisible = false;
                await first.PendingRelease;
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = bitmap.PixelSize);
            }
            finally
            {
                window.Close();
                File.Delete(path);
            }
            return true;
        }, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    public async Task ReplacingOrDeletingAFileDoesNotServeItsCachedImage()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryListImageLoadingTests));
        await session.Dispatch(async () =>
        {
            var path = Path.GetTempFileName();
            WriteImage(path, 100, 50);
            using var cache = PreviewImageProvider.CreateCache(null);
            cache.SetCapacity(1024 * 1024);
            var image = new PreviewImage(cache) { FilePath = path };
            var window = new Window { Content = image };
            try
            {
                window.Show();
                await image.PendingLoad;
                var old = image.Source!.Frames.Single();
                image.IsVisible = false;
                WriteImage(path, 200, 80);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));
                image.IsVisible = true;
                await image.PendingLoad;
                Assert.AreNotSame(old, image.Source!.Frames.Single());
                Assert.AreEqual(new PixelSize(200, 80), image.Source!.Frames.Single().PixelSize);
                await image.PendingRelease;
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = old.PixelSize);
                var missing = 0;
                image.FileMissing += (_, _) => missing++;
                image.IsVisible = false;
                File.Delete(path);
                image.IsVisible = true;
                await image.PendingLoad;
                Assert.IsNull(image.Source);
                Assert.AreEqual(1, missing);
            }
            finally
            {
                window.Close();
                File.Delete(path);
            }
            return true;
        }, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    [DataRow(MagickFormat.Gif)]
    [DataRow(MagickFormat.WebP)]
    public async Task AnimatedPreviewCompositesFramesAtPreviewSizeAndStopsWhenReplaced(MagickFormat format)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryListImageLoadingTests));
        await session.Dispatch(async () =>
        {
            var path = Path.GetTempFileName();
            var replacement = Path.GetTempFileName();
            using (var frames = new MagickImageCollection())
            {
                frames.Add(new MagickImage(MagickColors.Red, 8, 4) { AnimationDelay = 5, AnimationIterations = 0 });
                frames.Add(new MagickImage(MagickColors.Blue, 4, 4) { Page = new MagickGeometry(4, 0, 8, 4), AnimationDelay = 5 });
                if (format == MagickFormat.WebP)
                {
                    frames.Coalesce();
                    frames[0].Settings.SetDefine(MagickFormat.WebP, "lossless", true);
                }
                frames.Write(path, format);
            }
            WriteImage(replacement, 2, 2);
            var image = new PreviewImage { FilePath = path, DecodeBounds = new Size(4, 2) };
            var row = new Border { Child = image, Width = 4, Height = 2 };
            var viewport = new Canvas { ClipToBounds = true, Children = { row } };
            var window = new Window { Content = viewport, Width = 4, Height = 2, WindowDecorations = WindowDecorations.None };
            try
            {
                window.Show();
                await image.PendingLoad;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var colors = new HashSet<SKColor>();
                while (!colors.Contains(SKColors.Red) || !colors.Contains(SKColors.Blue))
                {
                    await Task.Delay(15, timeout.Token);
                    using var pixels = CapturePixels(window);
                    Assert.AreEqual(SKColors.Red, pixels.GetPixel(0, 0));
                    colors.Add(pixels.GetPixel(3, 0));
                }

                Canvas.SetTop(row, 4);
                window.UpdateLayout();
                Assert.IsFalse(image.IsPlaying);
                Canvas.SetTop(row, 0);
                window.UpdateLayout();
                Assert.IsTrue(image.IsPlaying);

                window.Hide();
                Assert.IsFalse(image.IsPlaying);
                window.Show();
                window.UpdateLayout();
                Assert.IsTrue(image.IsPlaying);
                var oldFrame = image.Source!.Frames[0];
                image.FilePath = replacement;
                await image.PendingLoad;
                await image.PendingRelease;
                Assert.AreEqual(new PixelSize(2, 2), image.Source!.Frames.Single().PixelSize);
                using var still = CapturePixels(window);
                Assert.AreEqual(SKColors.Green, still.GetPixel(2, 1));
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = oldFrame.PixelSize);
            }
            finally
            {
                window.Close();
                File.Delete(path);
                File.Delete(replacement);
            }
            return true;
        }, TestContext.CancellationTokenSource.Token);
    }

    private static SKBitmap CapturePixels(Window window)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.IsNotNull(frame);
        using var stream = new MemoryStream();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static void WriteImage(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Green);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
