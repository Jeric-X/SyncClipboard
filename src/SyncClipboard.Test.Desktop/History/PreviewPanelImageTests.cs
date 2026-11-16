using SyncClipboard.Desktop.Utilities;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using FluentAvalonia.Styling;
using SkiaSharp;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Desktop.Controls;
using SyncClipboard.Shared.Profiles;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test.Desktop.History;

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
                using var source = PreviewImageProvider.Decode(path, new PreviewImageProvider.DecodeOptions(new Size(2048, 2048)),
                    token: TestContext.CancellationTokenSource.Token).Source;
                var bitmap = source?.Frames.Single();
                Assert.IsNotNull(bitmap);
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
            var image = panel.FindControl<PreviewImage>("_PreviewImage")!;
            var window = new Window { Content = panel, Width = 850, Height = 530 };
            try
            {
                window.SetRenderScaling(renderScale);
                window.Show();
                var large = await WaitForImageAsync(image);
                Assert.AreEqual(new PixelSize(2048, 2048), large.PixelSize);
                record.PreviewImage = smallPath;
                await image.PendingRelease;
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = large.PixelSize);
                var small = await WaitForImageAsync(image);
                Assert.AreEqual(new PixelSize(32, 16), small.PixelSize);
                viewModel.ShowPreviewPanel = false;
                Assert.IsNull(image.Source);
                await image.PendingRelease;
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = small.PixelSize);
                viewModel.ShowPreviewPanel = true;
                var reopened = await WaitForImageAsync(image);
                Assert.AreEqual(new PixelSize(32, 16), reopened.PixelSize);
                window.Content = null;
                Assert.IsNull(image.Source);
                await image.PendingRelease;
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
                var preview = image.Source!.Frames.Single();
                Assert.AreEqual(new PixelSize(2048, 2048), preview.PixelSize);
                image.DecodeBounds = new Size(400, 200);
                image.ScaleWithRenderScaling = true;
                image.SizeToImage = true;
                await image.PendingRelease;
                Assert.ThrowsExactly<ObjectDisposedException>(() => _ = preview.PixelSize);
                await image.PendingLoad;
                var thumbnail = image.Source!.Frames.Single();
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

    private static async Task<Bitmap> WaitForImageAsync(PreviewImage image)
    {
        await image.PendingLoad;
        return image.Source!.Frames.Single();
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
