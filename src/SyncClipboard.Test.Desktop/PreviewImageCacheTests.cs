using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SyncClipboard.Desktop.Controls;
using SyncClipboard.Desktop.Utilities;

namespace SyncClipboard.Test.Desktop;

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
                var bitmap = (Bitmap)first.Source!;
                Assert.AreSame(bitmap, second.Source);
                first.IsVisible = false;
                Assert.IsNull(first.Source);
                Assert.AreEqual(new PixelSize(100, 50), bitmap.PixelSize);
                first.IsVisible = true;
                await first.PendingLoad;
                Assert.AreSame(bitmap, first.Source);

                panel.Children.Clear();
                Assert.IsNull(first.Source);
                Assert.IsNull(second.Source);
                Assert.AreEqual(new PixelSize(100, 50), bitmap.PixelSize);
                panel.Children.Add(first);
                await first.PendingLoad;
                Assert.AreSame(bitmap, first.Source);
                cache.SetCapacity(0);
                Assert.AreEqual(new PixelSize(100, 50), bitmap.PixelSize);
                first.IsVisible = false;
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
                var old = (Bitmap)image.Source!;
                image.IsVisible = false;
                WriteImage(path, 200, 80);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));
                image.IsVisible = true;
                await image.PendingLoad;
                Assert.AreNotSame(old, image.Source);
                Assert.AreEqual(new PixelSize(200, 80), ((Bitmap)image.Source!).PixelSize);
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

    private static void WriteImage(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
