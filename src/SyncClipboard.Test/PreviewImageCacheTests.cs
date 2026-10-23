using SyncClipboard.Core.Utilities.Images;

namespace SyncClipboard.Test;

[TestClass]
public class PreviewImageCacheTests
{
    public TestContext TestContext { get; set; } = null!;

    private sealed class Image(long bytes)
    {
        public long Bytes { get; } = bytes;
        public int Releases { get; private set; }
        public void Release() => Releases++;
    }

    private static PreviewImageCache<string, Image> Create(long capacity,
        Func<string, CancellationToken, Task<Image>>? load = null) =>
        new(capacity, load ?? ((_, _) => Task.FromResult(new Image(1024))), image => image.Bytes, image => image.Release());

    [TestMethod]
    public async Task LeasesShareImagesAndReleaseOnlyAfterLastConsumerAndEviction()
    {
        using var cache = Create(2048);
        var first = await cache.AcquireAsync("one", TestContext.CancellationTokenSource.Token);
        var second = await cache.AcquireAsync("one", TestContext.CancellationTokenSource.Token);
        var image = first.Image;
        Assert.AreSame(image, second.Image);
        first.Dispose();
        first.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = first.Image);
        cache.SetCapacity(0);
        Assert.AreEqual(0, image.Releases);
        second.Dispose();
        Assert.AreEqual(1, image.Releases);
        using var replacement = await cache.AcquireAsync("one", TestContext.CancellationTokenSource.Token);
        Assert.AreNotSame(image, replacement.Image);
    }

    [TestMethod]
    public async Task EvictsLeastRecentlyUsedIdleImageByBytes()
    {
        using var cache = Create(2048);
        var first = await cache.AcquireAsync("one", TestContext.CancellationTokenSource.Token);
        var firstImage = first.Image;
        first.Dispose();
        var second = await cache.AcquireAsync("two", TestContext.CancellationTokenSource.Token);
        var secondImage = second.Image;
        second.Dispose();
        using (var hit = await cache.AcquireAsync("one", TestContext.CancellationTokenSource.Token))
            Assert.AreSame(firstImage, hit.Image);
        using var third = await cache.AcquireAsync("three", TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(0, firstImage.Releases);
        Assert.AreEqual(1, secondImage.Releases);
        cache.SetCapacity(0);
        Assert.AreEqual(1, firstImage.Releases);
        Assert.AreEqual(0, third.Image.Releases);
    }

    [TestMethod]
    [DataRow(800, true)]
    [DataRow(801, false)]
    [DataRow(1200, false)]
    public async Task LargeImagesShareActiveLeasesWithoutEvictingIdleImages(long bytes, bool retained)
    {
        using var cache = Create(1000, (key, _) => Task.FromResult(new Image(key == "large" ? bytes : 256)));
        var token = TestContext.CancellationTokenSource.Token;
        var small = await cache.AcquireAsync("small", token);
        var smallImage = small.Image;
        small.Dispose();
        var first = await cache.AcquireAsync("large", token);
        var largeImage = first.Image;
        using (var second = await cache.AcquireAsync("large", token))
            Assert.AreSame(largeImage, second.Image);
        Assert.AreEqual(retained ? 1 : 0, smallImage.Releases);
        first.Dispose();
        Assert.AreEqual(retained ? 0 : 1, largeImage.Releases);
    }

    [TestMethod]
    public async Task BudgetChangesReclassifyActiveAndIdleImages()
    {
        using var cache = Create(2000, (key, _) => Task.FromResult(new Image(key == "large" ? 900 : 256)));
        var token = TestContext.CancellationTokenSource.Token;
        var small = await cache.AcquireAsync("small", token);
        var smallImage = small.Image;
        small.Dispose();
        var large = await cache.AcquireAsync("large", token);
        var largeImage = large.Image;
        cache.SetCapacity(1000);
        Assert.AreEqual(0, smallImage.Releases);
        Assert.AreEqual(0, largeImage.Releases);
        large.Dispose();
        Assert.AreEqual(1, largeImage.Releases);
        using (var hit = await cache.AcquireAsync("small", token))
            Assert.AreSame(smallImage, hit.Image);

        var growing = await cache.AcquireAsync("large", token);
        var growingImage = growing.Image;
        cache.SetCapacity(2000);
        growing.Dispose();
        Assert.AreEqual(0, growingImage.Releases);
        cache.SetCapacity(1000);
        Assert.AreEqual(1, growingImage.Releases);
        Assert.AreEqual(0, smallImage.Releases);
    }

    [TestMethod]
    public async Task OneCanceledWaiterDoesNotCancelSharedDecode()
    {
        var completion = new TaskCompletionSource<Image>();
        var count = 0;
        CancellationToken decodeToken = default;
        using var cache = Create(1024, (_, token) =>
        {
            count++;
            decodeToken = token;
            return completion.Task;
        });
        using var cancellation = new CancellationTokenSource();
        var canceled = cache.AcquireAsync("one", cancellation.Token);
        var surviving = cache.AcquireAsync("one", TestContext.CancellationTokenSource.Token);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => canceled);
        Assert.IsFalse(decodeToken.IsCancellationRequested);
        completion.SetResult(new Image(1024));
        using var lease = await surviving;
        Assert.AreEqual(1, count);
        Assert.AreEqual(0, lease.Image.Releases);
    }

    [TestMethod]
    public async Task AbandonedDecodeReleasesLateImageAndDoesNotReplaceNewRequest()
    {
        var completion = new TaskCompletionSource<Image>();
        var released = new TaskCompletionSource();
        var count = 0;
        CancellationToken decodeToken = default;
        using var cache = new PreviewImageCache<string, Image>(1024, (_, token) =>
        {
            decodeToken = token;
            return ++count == 1 ? completion.Task : Task.FromResult(new Image(1024));
        }, image => image.Bytes, image =>
        {
            image.Release();
            released.TrySetResult();
        });
        using var cancellation = new CancellationTokenSource();
        var canceled = cache.AcquireAsync("one", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => canceled);
        Assert.IsTrue(decodeToken.IsCancellationRequested);
        using var next = await cache.AcquireAsync("one", TestContext.CancellationTokenSource.Token);
        var abandoned = new Image(1024);
        completion.SetResult(abandoned);
        await released.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(1, abandoned.Releases);
        using var hit = await cache.AcquireAsync("one", TestContext.CancellationTokenSource.Token);
        Assert.AreSame(next.Image, hit.Image);
    }

    [TestMethod]
    public async Task FailedDecodeCanBeRetried()
    {
        var count = 0;
        using var cache = Create(1024, (_, _) => ++count == 1
            ? Task.FromException<Image>(new InvalidDataException()) : Task.FromResult(new Image(1024)));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => cache.AcquireAsync("one", TestContext.CancellationTokenSource.Token));
        using var lease = await cache.AcquireAsync("one", TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public async Task InvalidationAndShutdownPreserveActiveLeases()
    {
        using var cache = Create(2048);
        var old = await cache.AcquireAsync("one", TestContext.CancellationTokenSource.Token);
        var oldImage = old.Image;
        cache.Invalidate(key => key == "one");
        var fresh = await cache.AcquireAsync("one", TestContext.CancellationTokenSource.Token);
        var freshImage = fresh.Image;
        Assert.AreNotSame(oldImage, freshImage);
        cache.Dispose();
        Assert.AreEqual(0, oldImage.Releases);
        Assert.AreEqual(0, freshImage.Releases);
        old.Dispose();
        fresh.Dispose();
        Assert.AreEqual(1, oldImage.Releases);
        Assert.AreEqual(1, freshImage.Releases);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => cache.AcquireAsync("two", TestContext.CancellationTokenSource.Token));
    }

    [TestMethod]
    public void MetadataIsSharedAcrossDecodeSizesButNotFileVersions()
    {
        var cache = new PreviewImageMetadataCache<(int Width, int Height)>();
        var thumbnail = new PreviewImageKey("image", 100, 1, 5120, 200, 1, 5120, false);
        cache.Set(thumbnail, (800, 600));
        Assert.AreEqual((800, 600), cache.Get(thumbnail with { Width = 2048, Height = 2048 })!.Value);
        Assert.IsNull(cache.Get(thumbnail with { LastWriteTicks = 2 }));
    }

    [TestMethod]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    public void FileVersionPreservesAccessErrorsInsteadOfReportingMissingFiles()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("This test requires Unix directory permissions.");
            return;
        }
        if (Environment.UserName == "root")
            Assert.Inconclusive("Root bypasses the directory access restriction used by this test.");

        var directory = Directory.CreateTempSubdirectory();
        var mode = File.GetUnixFileMode(directory.FullName);
        var path = Path.Combine(directory.FullName, "image");
        try
        {
            File.WriteAllBytes(path, [1]);
            File.SetUnixFileMode(directory.FullName, UnixFileMode.None);
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => PreviewImageKey.Create(path, 400, 200, 1, 5120, false));
        }
        finally
        {
            File.SetUnixFileMode(directory.FullName, mode);
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void FileVersionAndDecodeSpecificationsDistinguishCacheEntries()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [1]);
            var first = PreviewImageKey.Create(path, 400, 200, 1, 5120, false);
            var larger = PreviewImageKey.Create(path, 400, 200, 2, 5120, false);
            Assert.AreNotEqual(first, larger);
            File.WriteAllBytes(path, [1, 2]);
            var replaced = PreviewImageKey.Create(path, 400, 200, 1, 5120, false);
            Assert.IsTrue(first.IsOlderVersionOf(replaced));
            File.Delete(path);
            Assert.ThrowsExactly<FileNotFoundException>(() => PreviewImageKey.Create(path, 400, 200, 1, 5120, false));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
