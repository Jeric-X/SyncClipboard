using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test;

[TestClass]
public class RemoteClipboardServerFactoryLifecycleTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Dispose_WaitsForServerCreationAndReleasesItsAdapter(bool blockFactory)
    {
        using var fixture = new Fixture();
        using var resume = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = CreateAdapter();
        void Block()
        {
            entered.SetResult();
            Assert.IsTrue(resume.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token));
        }
        if (!blockFactory)
        {
            adapter.Setup(a => a.SetConfig(It.IsAny<object>(), It.IsAny<SyncConfig>())).Callback(Block);
        }
        fixture.BuildAdapter = () =>
        {
            if (blockFactory) Block();
            return adapter.Object;
        };
        var resetting = Task.Run(fixture.Reset, TestContext.CancellationTokenSource.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        Task disposing;
        try
        {
            disposing = await AssertDisposeWaitsAsync(fixture.Factory);
        }
        finally
        {
            resume.Set();
        }
        await Task.WhenAll(resetting, disposing).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);

        adapter.As<IDisposable>().Verify(a => a.Dispose(), Times.Once);
        Assert.Throws<ObjectDisposedException>(() => _ = fixture.Factory.Current);
        Assert.Throws<ObjectDisposedException>(fixture.Reset);
    }

    [TestMethod]
    public async Task Dispose_WaitsForConfigurationUpdateAndIgnoresLaterUpdates()
    {
        using var fixture = new Fixture();
        var adapter = CreateAdapter();
        fixture.BuildAdapter = () => adapter.Object;
        fixture.Reset();
        using var resume = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.Setup(a => a.SetConfig(It.IsAny<object>(), It.IsAny<SyncConfig>())).Callback(() =>
        {
            entered.SetResult();
            Assert.IsTrue(resume.Wait(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token));
        });
        var updating = Task.Run(() => fixture.Config.SetConfig(new SyncConfig { IntervalTime = 11 }),
            TestContext.CancellationTokenSource.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        Task disposing;
        try
        {
            disposing = await AssertDisposeWaitsAsync(fixture.Factory);
        }
        finally
        {
            resume.Set();
        }
        await Task.WhenAll(updating, disposing).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        fixture.Config.SetConfig(new SyncConfig { IntervalTime = 12 });

        adapter.Verify(a => a.SetConfig(It.IsAny<object>(), It.IsAny<SyncConfig>()), Times.Exactly(2));
        adapter.As<IDisposable>().Verify(a => a.Dispose(), Times.Once);
    }

    [TestMethod]
    public void ChangeNotification_AllowsAnotherThreadToReadCurrent()
    {
        using var fixture = new Fixture();
        fixture.BuildAdapter = () => CreateAdapter().Object;
        IRemoteClipboardServer? observed = null;
        fixture.Factory.CurrentServerChanged += (_, _) =>
            observed = Task.Run(() => fixture.Factory.Current, TestContext.CancellationTokenSource.Token)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token).GetAwaiter().GetResult();

        fixture.Reset();

        Assert.AreSame(fixture.Factory.Current, observed);
    }

    [TestMethod]
    public void ChangeNotification_CanDisposeFactoryAndStopsLaterNotifications()
    {
        using var fixture = new Fixture();
        var adapter = CreateAdapter();
        fixture.BuildAdapter = () => adapter.Object;
        var laterNotifications = 0;
        fixture.Factory.CurrentServerChanged += (_, _) => fixture.Factory.Dispose();
        fixture.Factory.CurrentServerChanged += (_, _) => laterNotifications++;

        fixture.Reset();

        Assert.AreEqual(0, laterNotifications);
        adapter.As<IDisposable>().Verify(a => a.Dispose(), Times.Once);
        Assert.Throws<ObjectDisposedException>(() => _ = fixture.Factory.Current);
    }

    [TestMethod]
    public void Current_RejectsDisposalDuringInitialChangeNotification()
    {
        using var fixture = new Fixture();
        fixture.Factory.CurrentServerChanged += (_, _) => fixture.Factory.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = fixture.Factory.Current);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ReentrantDisposal_DoesNotPublishOrLeakNewAdapter(bool disposeInFactory)
    {
        using var fixture = new Fixture();
        var adapter = CreateAdapter();
        if (!disposeInFactory)
        {
            adapter.Setup(a => a.ApplyConfig()).Callback(fixture.Factory.Dispose);
        }
        fixture.BuildAdapter = () =>
        {
            if (disposeInFactory) fixture.Factory.Dispose();
            return adapter.Object;
        };

        Assert.Throws<ObjectDisposedException>(fixture.Reset);

        adapter.As<IDisposable>().Verify(a => a.Dispose(), Times.Once);
        Assert.Throws<ObjectDisposedException>(() => _ = fixture.Factory.Current);
    }

    private async Task<Task> AssertDisposeWaitsAsync(RemoteClipboardServerFactory factory)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposing = Task.Run(() =>
        {
            started.SetResult();
            factory.Dispose();
        }, TestContext.CancellationTokenSource.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        await Assert.ThrowsAsync<TimeoutException>(() =>
            disposing.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.CancellationTokenSource.Token));
        return disposing;
    }

    private static Mock<IStorageBasedServerAdapter> CreateAdapter()
    {
        var adapter = new Mock<IStorageBasedServerAdapter>();
        adapter.As<IDisposable>();
        adapter.Setup(a => a.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        adapter.Setup(a => a.TestConnectionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return adapter;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"RemoteFactoryTests-{Guid.NewGuid():N}");
        private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _services;
        public ConfigManager Config { get; }
        public RemoteClipboardServerFactory Factory { get; }
        public Func<IServerAdapter> BuildAdapter { get; set; } = null!;

        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "SyncClipboard.json");
            File.WriteAllText(path, """{"ConfigVersion":1}""");
            Config = new ConfigManager(path, new SyncClipboardConfigUpgrader());
            _services = new ServiceCollection()
                .AddSingleton(Config)
                .AddSingleton<AccountManager>()
                .AddSingleton(Mock.Of<ILogger>())
                .AddSingleton(Mock.Of<ITrayIcon>())
                .AddSingleton(Mock.Of<IProfileEnv>())
                .AddKeyedSingleton<ServerAdapterFactory>("test", (_, _) => _ => BuildAdapter())
                .BuildServiceProvider();
            Factory = new RemoteClipboardServerFactory(_services);
        }

        public void Reset() => Factory.ResetCurrentServer(
            new AccountConfig { AccountType = "test", AccountId = "test-account" }, new object());

        public void Dispose()
        {
            Factory.Dispose();
            _services.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
