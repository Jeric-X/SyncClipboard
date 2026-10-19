using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Imaging;
using SyncClipboard.WinUI3.Controls;
using SyncClipboard.WinUI3.Utilities;
using SyncClipboard.WinUI3.ValueConverters;
using System.Diagnostics;
using Windows.Graphics;
using Windows.Graphics.Imaging;

namespace SyncClipboard.Test.WinUI3;

[TestClass]
[DoNotParallelize]
public class HistoryImageTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(4096, 1024, false, 1.0, 2048, 512)]
    [DataRow(1024, 4096, false, 2.0, 512, 2048)]
    [DataRow(32, 16, false, 2.0, 32, 16)]
    [DataRow(2400, 1600, true, 1.0, 300, 200)]
    [DataRow(2400, 1600, true, 2.0, 600, 400)]
    [DataRow(8000, 100, true, 2.0, 5120, 64)]
    [DataRow(100, 8000, true, 2.0, 5, 400)]
    [DataRow(32, 16, true, 2.0, 32, 16)]
    public void DecodeSizesPreserveAspectRatioAndBoundPixels(int width, int height, bool thumbnail, double scale,
        int expectedWidth, int expectedHeight)
    {
        var size = HistoryImageSize.Calculate((uint)width, (uint)height, thumbnail, scale);
        Assert.AreEqual(expectedWidth, size.PixelWidth);
        Assert.AreEqual(expectedHeight, size.PixelHeight);
        Assert.AreEqual((double)width / height, size.Width / size.Height, 0.000001);
        Assert.IsLessThanOrEqualTo(thumbnail ? 200 : 2048, size.Height);
    }

    [TestMethod]
    [DataRow(0, 100)]
    [DataRow(100, 0)]
    [DataRow(1, 100000)]
    [DataRow(100000, 1)]
    public void InvalidOrSubpixelImagesAreRejected(int width, int height)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => HistoryImageSize.Calculate((uint)width, (uint)height, true, 1));
    }

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public async Task ImagesReleaseSourcesWhenHiddenReplacedOrUnloaded()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("WinUI image loading requires Windows.");
            return;
        }

        var executable = Path.ChangeExtension(typeof(HistoryImageTests).Assembly.Location, ".exe");
        using var process = Process.Start(new ProcessStartInfo(executable, "--history-image-test")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var token = TestContext.CancellationTokenSource.Token;
        var output = process.StandardOutput.ReadToEndAsync(token);
        var errors = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(60), token);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        Assert.AreEqual(0, process.ExitCode, await output + await errors);
    }

    internal static async Task RunImageChecksAsync()
    {
        var directory = Directory.CreateTempSubdirectory("winui-history-images-");
        var largePath = Path.Combine(directory.FullName, "large.png");
        var smallPath = Path.Combine(directory.FullName, "small.png");
        await CreateImageAsync(largePath, 4096, 1024);
        await CreateImageAsync(smallPath, 32, 16);
        var image = new Image();
        var parent = new Border { Child = image };
        var window = new Window { Content = parent };
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        var state = HistoryImageLoader.GetState(image);
        try
        {
            HistoryImageLoader.SetFilePath(image, largePath);
            Assert.IsNull(image.Source);
            window.AppWindow.Show(false);
            var large = await WaitForBitmapAsync(image);
            // WinUI reports the original dimensions through PixelWidth/Height even with scaled decoding.
            Assert.AreEqual(4096, large.PixelWidth);
            Assert.AreEqual(1024, large.PixelHeight);
            Assert.AreEqual(2048, large.DecodePixelWidth);
            Assert.AreEqual(512, large.DecodePixelHeight);

            image.Visibility = Visibility.Collapsed;
            Assert.IsNull(image.Source);
            HistoryImageLoader.SetFilePath(image, smallPath);
            await state.PendingLoad;
            Assert.IsNull(image.Source);
            image.Visibility = Visibility.Visible;
            var small = await WaitForBitmapAsync(image);
            Assert.AreEqual(32, small.PixelWidth);
            Assert.AreEqual(16, small.PixelHeight);
            Assert.AreEqual(32, image.MaxWidth);
            Assert.AreEqual(16, image.MaxHeight);

            HistoryImageLoader.SetFilePath(image, ConvertMethod.HistoryImagePath(largePath, true));
            Assert.IsNull(image.Source);
            HistoryImageLoader.SetFilePath(image, ConvertMethod.HistoryImagePath(largePath, false));
            HistoryImageLoader.SetIsThumbnail(image, true);
            var thumbnail = await WaitForBitmapAsync(image);
            Assert.IsLessThanOrEqualTo(5120, thumbnail.DecodePixelWidth);
            Assert.IsLessThanOrEqualTo((int)(200 * image.XamlRoot.RasterizationScale), thumbnail.DecodePixelHeight);
            Assert.AreEqual(200, image.Height);

            HistoryImageLoader.SetContainerRecycled(parent, true);
            Assert.IsTrue(image.IsLoaded);
            Assert.IsNull(image.Source);
            HistoryImageLoader.SetFilePath(image, smallPath);
            await state.PendingLoad;
            Assert.IsNull(image.Source);
            HistoryImageLoader.SetContainerRecycled(parent, false);
            var reused = await WaitForBitmapAsync(image);
            Assert.AreEqual(32, reused.PixelWidth);

            // Hide before the asynchronous header read completes: it must not restore Source later.
            HistoryImageLoader.SetFilePath(image, largePath);
            var pending = state.PendingLoad;
            image.Visibility = Visibility.Collapsed;
            Assert.IsNull(image.Source);
            await pending;
            Assert.IsNull(image.Source);
            image.Visibility = Visibility.Visible;
            await WaitForBitmapAsync(image);

            parent.Child = null;
            // Unloaded is dispatched after IsLoaded changes, on a subsequent UI turn.
            await WaitUntilAsync(() => image.Source is null);
            Assert.IsFalse(image.IsLoaded);
            parent.Child = image;
            await WaitForBitmapAsync(image);

            var missingCount = 0;
            state.FileMissing += (_, _) => missingCount++;
            HistoryImageLoader.SetFilePath(image, Path.Combine(directory.FullName, "missing.png"));
            await state.PendingLoad;
            Assert.AreEqual(1, missingCount);
            Assert.IsNull(image.Source);
            var invalidPath = Path.Combine(directory.FullName, "invalid.png");
            await File.WriteAllTextAsync(invalidPath, "not an image");
            HistoryImageLoader.SetFilePath(image, invalidPath);
            await state.PendingLoad;
            Assert.AreEqual(1, missingCount);
            Assert.IsNull(image.Source);

            await VerifyDecodeFailuresAsync(image, directory.FullName);
            await VerifyScrollPositionAsync(window, largePath);
            await VerifyRecyclingAsync(window, largePath);
        }
        finally
        {
            window.Close();
            directory.Delete(true);
        }
    }

    private static async Task VerifyDecodeFailuresAsync(Image image, string directory)
    {
        var path = Path.Combine(directory, "deleted-after-header.png");
        await CreateImageAsync(path, 400, 200);
        var state = HistoryImageLoader.GetState(image);
        var missingCount = 0;
        state.FileMissing += (_, _) => missingCount++;
        var sourceWasAssigned = false;
        var subscription = image.RegisterPropertyChangedCallback(Image.SourceProperty, (_, _) =>
        {
            if (image.Source is BitmapImage)
            {
                sourceWasAssigned = true;
                // Header reading has finished; the URI decoder has not opened the file yet.
                File.Delete(path);
            }
        });
        try
        {
            HistoryImageLoader.SetFilePath(image, path);
            await state.PendingLoad;
            await WaitUntilAsync(() => missingCount == 1);
            Assert.IsTrue(sourceWasAssigned);
            Assert.IsNull(image.Source);
            Assert.IsTrue(double.IsNaN(image.Width));
            Assert.IsTrue(double.IsNaN(image.Height));
        }
        finally
        {
            image.UnregisterPropertyChangedCallback(Image.SourceProperty, subscription);
        }

        var corruptPath = Path.Combine(directory, "corrupt-payload.png");
        await CreateImageAsync(corruptPath, 400, 200);
        sourceWasAssigned = false;
        subscription = image.RegisterPropertyChangedCallback(Image.SourceProperty, (_, _) =>
        {
            if (image.Source is BitmapImage)
            {
                sourceWasAssigned = true;
                // Preserve a real file, but replace its payload after the successful header read.
                File.WriteAllText(corruptPath, "corrupt image payload");
            }
        });
        try
        {
            HistoryImageLoader.SetFilePath(image, corruptPath);
            await state.PendingLoad;
            await WaitUntilAsync(() => image.Source is null);
            Assert.IsTrue(sourceWasAssigned);
            Assert.AreEqual(1, missingCount);
            Assert.IsTrue(double.IsNaN(image.Width));
            Assert.IsTrue(double.IsNaN(image.Height));
        }
        finally
        {
            image.UnregisterPropertyChangedCallback(Image.SourceProperty, subscription);
        }
    }

    private static async Task VerifyRecyclingAsync(Window window, string path)
    {
        var list = new HistoryListView
        {
            Width = 400,
            Height = 400,
            ItemTemplate = (DataTemplate)XamlReader.Load($"""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                              xmlns:controls="using:SyncClipboard.WinUI3.Controls">
                    <Image controls:HistoryImageLoader.IsThumbnail="True"
                           controls:HistoryImageLoader.FilePath="{path}" />
                </DataTemplate>
                """),
            ItemsSource = Enumerable.Range(0, 100).ToArray(),
        };
        var recycledCount = 0;
        list.ContainerContentChanging += (_, args) =>
        {
            if (args.InRecycleQueue && args.ItemContainer.ContentTemplateRoot is Image recycledImage)
            {
                recycledCount++;
                Assert.IsNull(recycledImage.Source);
            }
        };
        window.Content = list;
        await WaitUntilAsync(() => list.ContainerFromIndex(0) is ListViewItem { ContentTemplateRoot: Image });
        var first = (Image)((ListViewItem)list.ContainerFromIndex(0)).ContentTemplateRoot;
        await WaitForBitmapAsync(first);
        list.ScrollIntoView(80, ScrollIntoViewAlignment.Leading);
        await WaitUntilAsync(() => recycledCount > 0);
        await WaitUntilAsync(() => list.ContainerFromIndex(80) is ListViewItem { ContentTemplateRoot: Image });
        var reused = (Image)((ListViewItem)list.ContainerFromIndex(80)).ContentTemplateRoot;
        await WaitForBitmapAsync(reused);
        list.ScrollIntoView(0, ScrollIntoViewAlignment.Leading);
        await WaitUntilAsync(() => list.ContainerFromIndex(0) is ListViewItem { ContentTemplateRoot: Image });
        await WaitForBitmapAsync((Image)((ListViewItem)list.ContainerFromIndex(0)).ContentTemplateRoot);
    }

    private static async Task VerifyScrollPositionAsync(Window window, string path)
    {
        var rows = new StackPanel();
        var images = Enumerable.Range(0, 20).Select(_ => new Image()).ToArray();
        foreach (var image in images)
        {
            HistoryImageLoader.SetIsThumbnail(image, true);
            HistoryImageLoader.SetFilePath(image, path);
            rows.Children.Add(image);
        }
        var scroll = new ScrollViewer { Content = rows, Width = 400, Height = 400 };
        window.Content = scroll;
        await WaitUntilAsync(() => images.All(image => image.Source is BitmapImage { PixelWidth: > 0 }));
        scroll.UpdateLayout();
        scroll.ChangeView(null, 1200, null, disableAnimation: true);
        await WaitUntilAsync(() => scroll.VerticalOffset > 0);
        var offset = scroll.VerticalOffset;
        var extent = scroll.ExtentHeight;
        for (var cycle = 0; cycle < 2; cycle++)
        {
            window.AppWindow.Hide();
            scroll.UpdateLayout();
            Assert.AreEqual(extent, scroll.ExtentHeight, 0.01);
            Assert.AreEqual(offset, scroll.VerticalOffset, 0.01);

            window.AppWindow.Show(false);
            scroll.UpdateLayout();
            Assert.AreEqual(extent, scroll.ExtentHeight, 0.01);
            Assert.AreEqual(offset, scroll.VerticalOffset, 0.01);
            await WaitUntilAsync(() => images.All(image => image.Source is BitmapImage { PixelWidth: > 0 }));
            scroll.UpdateLayout();
            Assert.AreEqual(extent, scroll.ExtentHeight, 0.01);
            Assert.AreEqual(offset, scroll.VerticalOffset, 0.01);
        }
    }

    private static async Task<BitmapImage> WaitForBitmapAsync(Image image)
    {
        await WaitUntilAsync(() => image.Source is BitmapImage { PixelWidth: > 0 });
        return (BitmapImage)image.Source;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task CreateImageAsync(string path, uint width, uint height)
    {
        using var file = File.Create(path);
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96,
            new byte[width * height * 4]);
        await encoder.FlushAsync();
    }
}
