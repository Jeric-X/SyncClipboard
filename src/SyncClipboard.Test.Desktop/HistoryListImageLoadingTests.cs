using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using ObservableCollections;
using SyncClipboard.Desktop.Controls;
using SyncClipboard.Desktop.ValueConverters;
using System.Collections.ObjectModel;

namespace SyncClipboard.Test.Desktop;

[TestClass]
[DoNotParallelize]
public class HistoryListImageLoadingTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    public async Task HiddenImagesDoNotLoadUntilExpandedAndUseTheCurrentPath()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryListImageLoadingTests));
        await session.Dispatch(async () =>
        {
            var requested = new List<string>();
            var bitmaps = new List<TrackingBitmap>();
            var image = new PreviewImage((path, _, _) =>
            {
                requested.Add(path);
                var bitmap = CreateBitmap();
                bitmaps.Add(bitmap);
                return Task.FromResult(new PreviewImage.DecodedImage(bitmap, new Size(1, 1)));
            });
            var state = new ImageState { PreviewImage = "first.png" };
            using var visibleBinding = image.Bind(PreviewImage.IsVisibleProperty, new Binding(nameof(ImageState.IsCompactListMode))
            {
                Source = state,
                Converter = FuncConverter.IsFalse,
                FallbackValue = false
            });
            using var pathBinding = image.Bind(PreviewImage.FilePathProperty,
                new Binding(nameof(ImageState.PreviewImage)) { Source = state });
            var window = new Window { Content = image };
            try
            {
                window.Show();
                Assert.IsEmpty(requested);
                state.IsCompactListMode = true;
                state.PreviewImage = "current.png";
                Assert.IsEmpty(requested);

                state.IsCompactListMode = false;
                await image.PendingLoad;
                Assert.HasCount(1, requested);
                Assert.AreEqual("current.png", requested[0]);
                Assert.IsNotNull(image.Source);

                state.IsCompactListMode = true;
                state.PreviewImage = "next.png";
                Assert.IsNull(image.Source);
                Assert.AreEqual(1, bitmaps[0].DisposeCount);
                Assert.HasCount(1, requested);

                state.IsCompactListMode = false;
                await image.PendingLoad;
                Assert.HasCount(2, requested);
                Assert.AreEqual("next.png", requested[1]);
                window.Hide();
                state.IsCompactListMode = true;
                Assert.IsNull(image.Source);
                Assert.AreEqual(1, bitmaps[1].DisposeCount);
                state.PreviewImage = "hidden-window.png";
                window.Show();
                Assert.HasCount(2, requested);
                state.IsCompactListMode = false;
                await image.PendingLoad;
                Assert.HasCount(3, requested);
                Assert.AreEqual("hidden-window.png", requested[2]);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    public async Task RefreshingItemsReleasesOldBitmapsAndLoadsFreshThumbnails()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryListImageLoadingTests));
        await session.Dispatch(() =>
        {
            var bitmaps = new List<TrackingBitmap>();
            Application.Current!.Styles.Add(new FluentTheme());
            var items = new ObservableCollection<string> { "one.png", "two.png" };
            var list = new ListBox
            {
                ItemsSource = items,
                ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel()),
                ItemTemplate = new FuncDataTemplate<string>((path, _) => new PreviewImage((_, _, _) =>
                {
                    var bitmap = CreateBitmap();
                    bitmaps.Add(bitmap);
                    return Task.FromResult(new PreviewImage.DecodedImage(bitmap, new Size(1, 1)));
                })
                { FilePath = path })
            };
            var window = new Window { Content = list };
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.HasCount(2, bitmaps);
                items.Clear();
                window.UpdateLayout();
                Assert.IsTrue(bitmaps.All(bitmap => bitmap.DisposeCount == 1));
                items.Add("one.png");
                window.UpdateLayout();
                Assert.HasCount(3, bitmaps);
                Assert.AreEqual(0, bitmaps[2].DisposeCount);
            }
            finally
            {
                window.Close();
            }
            Assert.IsTrue(bitmaps.All(bitmap => bitmap.DisposeCount == 1));
        }, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    public async Task LateDecodeResultsAreDisposedAfterReplacementOrHiding()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryListImageLoadingTests));
        await session.Dispatch(async () =>
        {
            var requests = new List<TaskCompletionSource<PreviewImage.DecodedImage>>();
            var image = new PreviewImage((_, _, _) =>
            {
                var request = new TaskCompletionSource<PreviewImage.DecodedImage>();
                requests.Add(request);
                return request.Task;
            })
            { FilePath = "old.png" };
            var window = new Window { Content = image };
            try
            {
                window.Show();
                var oldLoad = image.PendingLoad;
                image.FilePath = "new.png";
                var newLoad = image.PendingLoad;
                window.Hide();
                var current = CreateBitmap();
                requests[1].SetResult(new(current, new Size(1, 1)));
                await newLoad;
                var stale = CreateBitmap();
                requests[0].SetResult(new(stale, new Size(1, 1)));
                await oldLoad;
                Assert.AreSame(current, image.Source);
                window.Show();
                Assert.AreSame(current, image.Source);
                Assert.HasCount(2, requests);
                Assert.AreEqual(1, stale.DisposeCount);

                image.FilePath = "pending.png";
                var pendingLoad = image.PendingLoad;
                Assert.AreEqual(1, current.DisposeCount);
                image.IsVisible = false;
                var hidden = CreateBitmap();
                requests[2].SetResult(new(hidden, new Size(1, 1)));
                await pendingLoad;
                Assert.IsNull(image.Source);
                Assert.AreEqual(1, hidden.DisposeCount);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    [DataRow(2400, 1600, 1.0, SKEncodedImageFormat.Png)]
    [DataRow(2400, 1600, 2.0, SKEncodedImageFormat.Jpeg)]
    [DataRow(8000, 100, 1.0, SKEncodedImageFormat.Png)]
    [DataRow(8000, 100, 2.0, SKEncodedImageFormat.Png)]
    [DataRow(100, 8000, 2.0, SKEncodedImageFormat.Png)]
    [DataRow(8000, 10, 2.0, SKEncodedImageFormat.Png)]
    [DataRow(32, 16, 2.0, SKEncodedImageFormat.Png)]
    public async Task DecodeBoundsPixelMemoryAndPreservesAspectRatio(int width, int height, double scale, SKEncodedImageFormat format)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryListImageLoadingTests));
        await session.Dispatch(() =>
        {
            var path = Path.GetTempFileName();
            try
            {
                using (var source = new SKBitmap(width, height))
                using (var data = source.Encode(format, 90))
                using (var stream = File.Create(path))
                {
                    data.SaveTo(stream);
                }
                var thumbnail = PreviewImage.Decode(path, new PreviewImage.DecodeOptions(new Size(5120, 200), scale, 5120));
                using var bitmap = thumbnail.Bitmap;
                Assert.IsLessThanOrEqualTo(5120, bitmap.PixelSize.Width);
                Assert.IsLessThanOrEqualTo((int)(200 * scale), bitmap.PixelSize.Height);
                Assert.IsLessThanOrEqualTo(width, bitmap.PixelSize.Width);
                Assert.IsLessThanOrEqualTo(height, bitmap.PixelSize.Height);
                Assert.AreEqual((double)width / height, thumbnail.DisplaySize.AspectRatio, 0.001);
                Assert.IsLessThanOrEqualTo(5120, thumbnail.DisplaySize.Width);
                Assert.IsLessThanOrEqualTo(200, thumbnail.DisplaySize.Height);
                if (width == 8000 && height == 100)
                {
                    Assert.AreEqual(new PixelSize(5120, 64), bitmap.PixelSize);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    public async Task MissingAndInvalidFilesLeaveThePreviewEmpty()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryListImageLoadingTests));
        await session.Dispatch(async () =>
        {
            var path = Path.GetTempFileName();
            var image = new PreviewImage { FilePath = path, DecodeBounds = new Size(5120, 200), MaxPixelWidth = 5120, ScaleWithRenderScaling = true, SizeToImage = true };
            var window = new Window { Content = image };
            try
            {
                window.Show();
                await image.PendingLoad;
                Assert.IsNull(image.Source);
                image.FilePath = path + ".missing";
                await image.PendingLoad;
                Assert.IsNull(image.Source);
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
    public async Task ReloadReleasesRealDecodedImagesWithoutGarbageCollection()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryListImageLoadingTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentTheme());
            var directory = Directory.CreateTempSubdirectory("history-images-");
            var paths = Enumerable.Range(0, 12).Select(index => Path.Combine(directory.FullName, $"{index}.png")).ToArray();
            var records = new ObservableList<string>();
            using var view = records.CreateView(path => path);
            using var items = view.ToNotifyCollectionChanged();
            var list = new ListBox
            {
                ItemsSource = items,
                ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel()),
                ItemTemplate = new FuncDataTemplate<string>((path, _) => new PreviewImage { FilePath = path, DecodeBounds = new Size(5120, 200), MaxPixelWidth = 5120, ScaleWithRenderScaling = true, SizeToImage = true })
            };
            var window = new Window { Content = list, Width = 850, Height = 530 };
            try
            {
                using (var source = new SKBitmap(1920, 1080))
                using (var data = source.Encode(SKEncodedImageFormat.Png, 100))
                {
                    foreach (var path in paths)
                    {
                        using var file = File.Create(path);
                        data.SaveTo(file);
                    }
                }
                window.SetRenderScaling(2);
                window.Show();
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    records.Clear();
                    window.UpdateLayout();
                    records.AddRange(paths);
                    var images = await LoadImagesAsync(12);
                    var oldBitmaps = images.Select(image => (Bitmap)image.Source!).ToArray();
                    records.Clear();
                    window.UpdateLayout();
                    // Retain managed references deliberately: native pixels must be released
                    // by removing the rows, without relying on collection/finalization.
                    foreach (var bitmap in oldBitmaps)
                    {
                        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = bitmap.PixelSize);
                    }
                    Assert.IsTrue(images.All(image => image.Source is null));
                    records.AddRange(paths.Take(3));
                    var reloaded = await LoadImagesAsync(3);
                    Assert.IsTrue(reloaded.All(image => !oldBitmaps.Contains(image.Source)));
                }
            }
            finally
            {
                window.Close();
                directory.Delete(true);
            }
            return true;

            async Task<PreviewImage[]> LoadImagesAsync(int count)
            {
                window.UpdateLayout();
                var images = list.GetVisualDescendants().OfType<PreviewImage>().ToArray();
                Assert.HasCount(count, images);
                await Task.WhenAll(images.Select(image => image.PendingLoad));
                Assert.IsTrue(images.All(image => image.Source is Bitmap));
                return images;
            }
        }, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    public async Task ReopeningWindowReusesImagesAndKeepsScrollPosition()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryListImageLoadingTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentTheme());
            var bitmaps = new List<TrackingBitmap>();
            var list = new ListBox
            {
                ItemsSource = Enumerable.Range(0, 20).Select(index => index.ToString()).ToArray(),
                AutoScrollToSelectedItem = false,
                ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel()),
                ItemTemplate = new FuncDataTemplate<string>((path, _) => new PreviewImage((file, _, _) =>
                {
                    var bitmap = CreateBitmap();
                    bitmaps.Add(bitmap);
                    return Task.FromResult(new PreviewImage.DecodedImage(bitmap, new Size(300, 80 + int.Parse(file) * 4)));
                })
                { FilePath = path, SizeToImage = true })
            };
            var window = new Window { Content = list, Width = 450, Height = 400 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var images = list.GetVisualDescendants().OfType<PreviewImage>().ToArray();
                Assert.HasCount(20, images);
                await Task.WhenAll(images.Select(image => image.PendingLoad));
                var sources = images.Select(image => image.Source).ToArray();
                var loadCount = bitmaps.Count;
                var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                scroll.Offset = new Vector(0, 1200);
                window.UpdateLayout();
                var offset = scroll.Offset.Y;
                var extent = scroll.Extent.Height;
                Assert.IsGreaterThan(0, offset);
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    window.Hide();
                    CollectionAssert.AreEqual(sources, images.Select(image => image.Source).ToArray());
                    Assert.IsTrue(bitmaps.All(bitmap => bitmap.DisposeCount == 0));
                    window.Show();
                    window.UpdateLayout();
                    Assert.HasCount(loadCount, bitmaps);
                    CollectionAssert.AreEqual(sources, images.Select(image => image.Source).ToArray());
                    Assert.AreEqual(extent, scroll.Extent.Height, 0.01);
                    Assert.AreEqual(offset, scroll.Offset.Y, 0.01);
                }
                window.Content = null;
                Assert.IsTrue(bitmaps.All(bitmap => bitmap.DisposeCount == 1));
            }
            finally
            {
                window.Close();
            }
            return true;
        }, TestContext.CancellationTokenSource.Token);
    }

    private static TrackingBitmap CreateBitmap()
    {
        using var source = new SKBitmap(1, 1);
        using var data = source.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        return new TrackingBitmap(stream);
    }

    private sealed class TrackingBitmap(Stream stream) : Bitmap(stream)
    {
        public int DisposeCount { get; private set; }

        public override void Dispose()
        {
            DisposeCount++;
            base.Dispose();
        }
    }

    private sealed class ImageState : ObservableObject
    {
        private string? previewImage;
        private bool? isCompactListMode;

        public string? PreviewImage
        {
            get => previewImage;
            set => SetProperty(ref previewImage, value);
        }

        public bool? IsCompactListMode
        {
            get => isCompactListMode;
            set => SetProperty(ref isCompactListMode, value);
        }
    }
}
