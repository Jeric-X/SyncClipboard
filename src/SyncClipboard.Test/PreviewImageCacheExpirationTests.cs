using Moq;
using SyncClipboard.Core.Utilities.Images;

namespace SyncClipboard.Test;

[TestClass]
public class PreviewImageCacheExpirationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ExpirationStartsAtLastReturnAndResetsAfterReuse()
    {
        var clock = new PreviewImageTestTimeProvider();
        var released = new List<object>();
        using var cache = new PreviewImageCache<string, object>(10000, (_, _) => Task.FromResult(new object()),
            _ => 1024, released.Add, clock);
        var token = TestContext.CancellationTokenSource.Token;
        var first = await cache.AcquireAsync("image", token);
        var second = await cache.AcquireAsync("image", token);
        var image = first.Image;
        first.Dispose();
        clock.Minutes = 65;
        clock.Scan();
        Assert.IsEmpty(released);
        second.Dispose();
        Assert.AreEqual(TimeSpan.FromMinutes(5), clock.DueTime);
        Assert.AreEqual(TimeSpan.FromMinutes(5), clock.Period);

        clock.Minutes = 120;
        clock.Scan();
        Assert.IsEmpty(released);
        var reused = await cache.AcquireAsync("image", token);
        Assert.AreSame(image, reused.Image);
        clock.Minutes = 185;
        clock.Scan();
        Assert.IsEmpty(released);
        clock.Minutes = 190;
        reused.Dispose();
        clock.Minutes = 245;
        clock.Scan();
        Assert.IsEmpty(released);
        clock.Minutes = 250;
        clock.Scan();
        CollectionAssert.AreEqual(new[] { image }, released);
        using var replacement = await cache.AcquireAsync("image", token);
        Assert.AreNotSame(image, replacement.Image);
    }

    [TestMethod]
    public async Task DisabledExpirationStillHonorsBudgetAndShutdownStopsScanning()
    {
        var clock = new PreviewImageTestTimeProvider();
        var released = new List<object>();
        using var cache = new PreviewImageCache<string, object>(10000, (_, _) => Task.FromResult(new object()),
            _ => 1024, released.Add, clock);
        var token = TestContext.CancellationTokenSource.Token;
        cache.SetIdleTimeout(TimeSpan.Zero);
        var idle = await cache.AcquireAsync("idle", token);
        var idleImage = idle.Image;
        idle.Dispose();
        var active = await cache.AcquireAsync("active", token);
        var activeImage = active.Image;
        clock.Minutes = 1440;
        clock.Scan();
        Assert.IsEmpty(released);
        cache.SetCapacity(0);
        CollectionAssert.AreEqual(new[] { idleImage }, released);
        cache.Dispose();
        clock.Timer.Verify(timer => timer.Dispose(), Times.Once);
        clock.Scan(); // A callback already queued before disposal must be harmless.
        CollectionAssert.AreEqual(new[] { idleImage }, released);
        active.Dispose();
        CollectionAssert.AreEqual(new[] { idleImage, activeImage }, released);
    }
}

internal sealed class PreviewImageTestTimeProvider : TimeProvider
{
    private TimerCallback? _callback;
    private object? _state;
    public long Minutes { get; set; }
    public TimeSpan DueTime { get; private set; }
    public TimeSpan Period { get; private set; }
    public Mock<ITimer> Timer { get; } = new();
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => TimeSpan.FromMinutes(Minutes).Ticks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _callback = callback;
        _state = state;
        DueTime = dueTime;
        Period = period;
        return Timer.Object;
    }

    public void Scan() => _callback?.Invoke(_state);
}
