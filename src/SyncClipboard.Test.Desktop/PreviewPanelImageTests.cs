using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using SkiaSharp;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Desktop.Controls;
using SyncClipboard.Shared.Profiles;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test.Desktop;

[TestClass]
[DoNotParallelize]
public class PreviewPanelImageTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    [DataRow(4096, 4096, 2048, 2048, SKEncodedImageFormat.Png)]
    [DataRow(4096, 4096, 2048, 2048, SKEncodedImageFormat.Jpeg)]
    [DataRow(4096, 1024, 2048, 512, SKEncodedImageFormat.Png)]
    [DataRow(1024, 4096, 512, 2048, SKEncodedImageFormat.Png)]
    [DataRow(1024, 1024, 1024, 1024, SKEncodedImageFormat.Png)]
    [DataRow(2048, 2048, 2048, 2048, SKEncodedImageFormat.Png)]
    public async Task DecodeLimitsBothDimensionsWithoutUpscaling(
        int width, int height, int expectedWidth, int expectedHeight, SKEncodedImageFormat format)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PreviewPanelImageTests));
        await session.Dispatch(() =>
        {
            var path = CreateImage(width, height, format);
            try
            {
                using var bitmap = PreviewImage.Decode(path, new PreviewImage.DecodeOptions(new Size(2048, 2048))).Bitmap;
                Assert.AreEqual(new PixelSize(expectedWidth, expectedHeight), bitmap.PixelSize);
                Assert.IsLessThanOrEqualTo(16L * 1024 * 1024, (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4);
            }
            finally
            {
                File.Delete(path);
            }
        }, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(2.0)]
    public async Task PanelLimitsPixelsAndDisposesImagesOnReplacementAndHiding(double renderScale)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PreviewPanelImageTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var configPath = Path.GetTempFileName();
            File.WriteAllText(configPath, "{}");
            var viewModel = (HistoryViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HistoryViewModel));
            typeof(HistoryViewModel).GetField("runtimeConfig", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(viewModel, new ConfigBase(configPath));
            viewModel.ShowPreviewPanel = true;
            Assert.IsTrue(viewModel.ShowPreviewPanel);
            var path = CreateImage(4096, 4096, SKEncodedImageFormat.Png);
            var smallPath = CreateImage(32, 16, SKEncodedImageFormat.Png);
            var record = (HistoryRecordVM)RuntimeHelpers.GetUninitializedObject(typeof(HistoryRecordVM));
            record.Type = ProfileType.Image;
            record.PreviewImage = path;
            record.Text = string.Empty;
            var panel = new PreviewPanel { ViewModel = viewModel, SelectedItem = record };
            var image = panel.FindControl<Image>("_PreviewImage")!;
            var window = new Window { Content = panel, Width = 850, Height = 530 };
            try
            {
                window.SetRenderScaling(renderScale);
                window.Show();
                var large = await WaitForImageAsync(image);
                Assert.AreEqual(new PixelSize(2048, 2048), large.PixelSize);
                record.PreviewImage = smallPath;
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = large.PixelSize);
                var small = await WaitForImageAsync(image);
                Assert.AreEqual(new PixelSize(32, 16), small.PixelSize);
                viewModel.ShowPreviewPanel = false;
                Assert.IsNull(image.Source);
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = small.PixelSize);
                viewModel.ShowPreviewPanel = true;
                var reopened = await WaitForImageAsync(image);
                Assert.AreEqual(new PixelSize(32, 16), reopened.PixelSize);
                window.Content = null;
                Assert.IsNull(image.Source);
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = reopened.PixelSize);
            }
            finally
            {
                window.Close();
                File.Delete(path);
                File.Delete(smallPath);
                File.Delete(configPath);
            }
            return true;
        }, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    [DataRow(0, 10485759L, true)]
    [DataRow(1, 10485759L, true)]
    [DataRow(2, 10485759L, true)]
    [DataRow(0, 10485760L, false)]
    [DataRow(1, 10485760L, false)]
    [DataRow(2, 10485760L, false)]
    [DataRow(0, 10485761L, false)]
    public async Task HeaderFailureOnlyFallsBackForFilesBelowTenMiB(int failure, long fileLength, bool shouldDisplay)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PreviewPanelImageTests));
        await session.Dispatch(async () =>
        {
            var path = CreateImage(1024, 512, SKEncodedImageFormat.Png);
            using (var file = File.OpenWrite(path))
            {
                file.SetLength(fileLength);
            }
            // Keep a decodable file so an unintended full decode above the limit would display it.
            var image = new PreviewImage((file, options, _) => Task.FromResult(PreviewImage.Decode(file, options, stream =>
            {
                stream.Position = 16;
                return failure switch
                {
                    0 => null,
                    1 => throw new InvalidDataException("Header unavailable"),
                    _ => new PixelSize(0, 0)
                };
            })))
            { FilePath = path, DecodeBounds = new Size(400, 200), SizeToImage = true };
            var window = new Window { Content = image };
            try
            {
                window.Show();
                await image.PendingLoad;
                if (shouldDisplay)
                {
                    var bitmap = (Bitmap)image.Source!;
                    Assert.IsNotNull(bitmap);
                    Assert.AreEqual(new PixelSize(400, 200), bitmap.PixelSize);
                    Assert.AreEqual(400, image.Width);
                    Assert.AreEqual(200, image.Height);
                }
                else
                {
                    Assert.IsNull(image.Source);
                }
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
    public async Task ReadableHeaderAllowsLargeFilesAndChangingBoundsReleasesPreviousBitmap()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PreviewPanelImageTests));
        await session.Dispatch(async () =>
        {
            var path = CreateImage(4096, 4096, SKEncodedImageFormat.Png);
            using (var file = File.OpenWrite(path))
            {
                file.SetLength(10L * 1024 * 1024);
            }
            var image = new PreviewImage { FilePath = path };
            var window = new Window { Content = image };
            try
            {
                window.SetRenderScaling(2);
                window.Show();
                await image.PendingLoad;
                var preview = (Bitmap)image.Source!;
                Assert.AreEqual(new PixelSize(2048, 2048), preview.PixelSize);
                image.DecodeBounds = new Size(400, 200);
                image.ScaleWithRenderScaling = true;
                image.SizeToImage = true;
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = preview.PixelSize);
                await image.PendingLoad;
                var thumbnail = (Bitmap)image.Source!;
                Assert.AreEqual(new PixelSize(400, 400), thumbnail.PixelSize);
                Assert.AreEqual(200, image.Width);
                Assert.AreEqual(200, image.Height);
            }
            finally
            {
                window.Close();
                File.Delete(path);
            }
            return true;
        }, TestContext.CancellationTokenSource.Token);
    }

    private async Task<Bitmap> WaitForImageAsync(Image image)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (image.Source is not Bitmap)
        {
            await Task.Delay(10, timeout.Token);
            Dispatcher.UIThread.RunJobs();
        }
        return (Bitmap)image.Source;
    }

    private static string CreateImage(int width, int height, SKEncodedImageFormat format)
    {
        var path = Path.GetTempFileName();
        using var source = new SKBitmap(width, height);
        using var data = source.Encode(format, 90);
        using var file = File.Create(path);
        data.SaveTo(file);
        return path;
    }
}
