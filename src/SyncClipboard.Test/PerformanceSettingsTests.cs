using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Images;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test;

[TestClass]
public class PerformanceSettingsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PerformanceSettingsPersistIndependentlyAndUpdateTheCache()
    {
        using var configuration = new ConfigurationTestServices();
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "config.json");
            var config = new ConfigManager(path, configuration.Upgrader);
            config.SetConfig(new HistoryConfig { MaxItemCount = 500 });
            var registrations = new ServiceCollection().AddSingleton(config);
            AppCore.ConfigurateViewModels(registrations);
            using var services = registrations.BuildServiceProvider();
            var vm = services.GetRequiredService<AdvancedSettingViewModel>();
            Assert.AreEqual(200u, vm.HistoryImageCacheSizeMB);
            Assert.AreEqual(10u, vm.HistoryImageCacheIdleTimeoutMinutes);
            Assert.AreEqual(1, vm.HistoryImageLoadConcurrency);

            var released = 0;
            var clock = new PreviewImageTestTimeProvider();
            using var cache = new PreviewImageCache<string, object>(0, (_, _) => Task.FromResult(new object()),
                _ => 160_000_000, _ => released++, clock);
            cache.FollowPerformanceConfiguration(config);
            using (var lease = await cache.AcquireAsync("image", TestContext.CancellationTokenSource.Token))
                Assert.IsNotNull(lease.Image);
            Assert.AreEqual(0, released);
            vm.HistoryImageCacheIdleTimeoutMinutes = 0;
            clock.Minutes = 120;
            clock.Scan();
            Assert.AreEqual(0, released);
            vm.HistoryImageCacheIdleTimeoutMinutes = 30;
            Assert.AreEqual(0, released);
            clock.Scan();
            Assert.AreEqual(1, released);
            using (var lease = await cache.AcquireAsync("image", TestContext.CancellationTokenSource.Token))
                Assert.IsNotNull(lease.Image);
            vm.HistoryImageCacheSizeMB = 0;
            Assert.AreEqual(2, released);
            vm.HistoryImageLoadConcurrency = 3;
            var saved = new ConfigManager(path, configuration.Upgrader);
            Assert.AreEqual(0u, saved.GetConfig<PerformanceConfig>().HistoryImageCacheSizeMB);
            Assert.AreEqual(3, saved.GetConfig<PerformanceConfig>().HistoryImageLoadConcurrency);
            Assert.AreEqual(30u, saved.GetConfig<PerformanceConfig>().HistoryImageCacheIdleTimeoutMinutes);
            Assert.AreEqual(500u, saved.GetConfig<HistoryConfig>().MaxItemCount);

            config.SetConfig(new PerformanceConfig { HistoryImageCacheSizeMB = 200, HistoryImageLoadConcurrency = 2 });
            Assert.AreEqual(200u, vm.HistoryImageCacheSizeMB);
            Assert.AreEqual(2, vm.HistoryImageLoadConcurrency);
            Assert.AreEqual(10u, vm.HistoryImageCacheIdleTimeoutMinutes);
            Assert.AreEqual(200u, config.GetConfig<PerformanceConfig>().HistoryImageCacheSizeMB);
            vm.HistoryImageLoadConcurrency = 0;
            Assert.AreEqual(1, vm.HistoryImageLoadConcurrency);
            Assert.AreEqual(1, config.GetConfig<PerformanceConfig>().HistoryImageLoadConcurrency);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task LoadingLimitCanGrowAndShrinkWithoutInterruptingActiveWork()
    {
        var limiter = new PreviewImageLoadLimiter();
        var token = TestContext.CancellationTokenSource.Token;
        using var first = await limiter.AcquireAsync(token);
        var secondTask = limiter.AcquireAsync(token);
        Assert.IsFalse(secondTask.IsCompleted);
        limiter.SetLimit(2);
        using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5), token);
        limiter.SetLimit(1);
        var thirdTask = limiter.AcquireAsync(token);
        first.Dispose();
        first.Dispose();
        Assert.IsFalse(thirdTask.IsCompleted);
        second.Dispose();
        using var third = await thirdTask.WaitAsync(TimeSpan.FromSeconds(5), token);
    }

    [TestMethod]
    public async Task CancelingAQueuedLoadDoesNotConsumeCapacity()
    {
        var limiter = new PreviewImageLoadLimiter();
        var token = TestContext.CancellationTokenSource.Token;
        using var first = await limiter.AcquireAsync(token);
        using var cancellation = new CancellationTokenSource();
        var queued = limiter.AcquireAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => queued);
        first.Dispose();
        using var next = await limiter.AcquireAsync(token).WaitAsync(TimeSpan.FromSeconds(5), token);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => limiter.SetLimit(0));
    }
}
