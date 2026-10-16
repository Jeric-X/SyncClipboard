using Moq;
using NativeNotification.Common;
using NativeNotification.Interface;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileNotificationHelperTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task MissingDurationDefaultsToTenSeconds()
    {
        using var fixture = new Fixture();

        var notification = await fixture.NotifyAsync(TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(TimeSpan.FromSeconds(10), notification.Options?.Duration);
        Assert.AreEqual("test notification", notification.Message);
        Assert.IsNotNull(notification.ContentAction);
        Assert.IsNotEmpty(notification.Buttons);
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task DurationIsReadFromPersistedConfiguration(int seconds)
    {
        using var fixture = new Fixture();
        fixture.Config.SetConfig(new SyncConfig { NotificationDurationSeconds = seconds });
        fixture.Config.Reload();

        var notification = await fixture.NotifyAsync(TestContext.CancellationTokenSource.Token);

        if (seconds > 0)
        {
            Assert.AreEqual(TimeSpan.FromSeconds(seconds), notification.Options?.Duration);
        }
        else
        {
            Assert.IsNull(notification.Options);
        }
    }

    [TestMethod]
    public async Task PositiveDurationRemovesNotification()
    {
        using var fixture = new Fixture();
        fixture.Config.SetConfig(new SyncConfig { NotificationDurationSeconds = 1 });
        var token = TestContext.CancellationTokenSource.Token;
        var notification = await fixture.NotifyAsync(token);

        await notification.Removed.Task.WaitAsync(TimeSpan.FromSeconds(5), token);

        Assert.IsFalse(notification.IsAlive);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(10)]
    public async Task PreviousExpirationDoesNotRemoveReplacement(int replacementDuration)
    {
        using var fixture = new Fixture();
        fixture.Config.SetConfig(new SyncConfig { NotificationDurationSeconds = 1 });
        var token = TestContext.CancellationTokenSource.Token;
        var first = await fixture.NotifyAsync(token);
        fixture.Config.SetConfig(new SyncConfig { NotificationDurationSeconds = replacementDuration });
        var second = await fixture.NotifyAsync(token);
        Assert.IsFalse(first.IsAlive);

        // The native library still runs the first notification's expiry callback after replacement.
        await first.RemovedAgain.Task.WaitAsync(TimeSpan.FromSeconds(5), token);

        Assert.IsTrue(second.IsAlive);
        Assert.IsFalse(second.Removed.Task.IsCompleted);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();
        private readonly ConfigurationTestServices _configuration = new();
        private readonly ProfileNotificationHelper _helper;
        private TaskCompletionSource<TestNotification> _shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConfigManager Config { get; }

        public Fixture()
        {
            Config = new ConfigManager(Path.Combine(_directory.FullName, "config.json"), _configuration.Upgrader);
            var manager = new Mock<INotificationManager>();
            manager.Setup(instance => instance.Create()).Returns(() => new TestNotification(item => _shown.TrySetResult(item)));
            var profileEnv = Mock.Of<IProfileEnv>(env => env.GetPersistentDir() == _directory.FullName);
            _helper = new ProfileNotificationHelper(manager.Object, new ProfileActionBuilder(null!, profileEnv), Config);
        }

        public async Task<TestNotification> NotifyAsync(CancellationToken token)
        {
            _shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _helper.Notify(new TextProfile("test notification"), token);
            return await _shown.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        }

        public void Dispose()
        {
            _configuration.Dispose();
            _directory.Delete(true);
        }
    }

    // Use the package's real expiry implementation without depending on a desktop notification service.
    private sealed class TestNotification(Action<TestNotification> onShow) : INotification
    {
        private ExpirationHelper? _expiration;
        private int _removeCount;
        public string? Title { get; set; }
        public string? Message { get; set; }
        public Uri? Image { get; set; }
        public List<ActionButton> Buttons { get; set; } = [];
        public Action? ContentAction { get; set; }
        public string NotificationId { get; } = Guid.NewGuid().ToString();
        public bool IsAlive { get; private set; }
        public bool IsCreatedByCurrentProcess => true;
        public NotificationDeliverOption? Options { get; private set; }
        public TaskCompletionSource Removed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RemovedAgain { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Show(NotificationDeliverOption? config = null)
        {
            Options = config;
            IsAlive = true;
            _expiration ??= new ExpirationHelper(this);
            _expiration.SetNoficifationDuration(config);
            onShow(this);
        }

        public bool Update() => IsAlive;

        public void Remove()
        {
            IsAlive = false;
            if (Interlocked.Increment(ref _removeCount) == 1)
            {
                Removed.TrySetResult();
            }
            else
            {
                RemovedAgain.TrySetResult();
            }
        }
    }
}
