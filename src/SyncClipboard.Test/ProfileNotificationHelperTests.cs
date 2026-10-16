using Moq;
using NativeNotification.Interface;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Profiles.Models;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileNotificationHelperTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task MissingDurationClosesAfterTenSeconds()
    {
        using var fixture = new Fixture();
        await fixture.NotifyAsync(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual("test notification", fixture.Notification.Object.Message);
        Assert.IsNotNull(fixture.Notification.Object.ContentAction);
        Assert.IsNotEmpty(fixture.Notification.Object.Buttons);

        fixture.Clock.Advance(TimeSpan.FromSeconds(9));
        Assert.IsTrue(fixture.IsAlive);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsFalse(fixture.IsAlive);
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
        await fixture.NotifyAsync(TestContext.CancellationTokenSource.Token);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(fixture.IsAlive);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(seconds <= 0, fixture.IsAlive);
        fixture.Clock.Advance(TimeSpan.FromDays(1));
        Assert.AreEqual(seconds <= 0, fixture.IsAlive);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(10)]
    public async Task NewNotificationResetsAutomaticClose(int replacementDuration)
    {
        using var fixture = new Fixture();
        var token = TestContext.CancellationTokenSource.Token;
        await fixture.NotifyAsync(token);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        fixture.Config.SetConfig(new SyncConfig { NotificationDurationSeconds = replacementDuration });
        await fixture.NotifyAsync(token);

        fixture.Clock.Advance(TimeSpan.FromSeconds(4));
        Assert.IsTrue(fixture.IsAlive);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.AreEqual(replacementDuration <= 0, fixture.IsAlive);
    }

    [TestMethod]
    public async Task SupersededLocalizationDoesNotShowOldContent()
    {
        using var fixture = new Fixture();
        var token = TestContext.CancellationTokenSource.Token;
        var localized = new TaskCompletionSource<ProfileLocalInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldProfile = new Mock<Profile>();
        oldProfile.SetupGet(profile => profile.ShortDisplayText).Returns("old content");
        oldProfile.Setup(profile => profile.Localize(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(localized.Task);
        var pending = fixture.Helper.NotifyAsync(oldProfile.Object, token);
        Assert.IsFalse(pending.IsCompleted);

        await fixture.Helper.NotifyAsync(new TextProfile("new content"), token);
        localized.SetResult(new ProfileLocalInfo { Text = "old content" });
        await pending.WaitAsync(TimeSpan.FromSeconds(5), token);

        Assert.IsTrue(fixture.IsAlive);
        Assert.AreEqual("new content", fixture.Notification.Object.Message);
        fixture.Notification.Verify(notification => notification.Show(It.IsAny<NotificationDeliverOption>()), Times.Once);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();
        private readonly ConfigurationTestServices _configuration = new();
        public Mock<INotification> Notification { get; } = new();
        public ManualTimeProvider Clock { get; } = new();
        public ConfigManager Config { get; }
        public ProfileNotificationHelper Helper { get; }
        public bool IsAlive { get; private set; }

        public Fixture()
        {
            Config = new ConfigManager(Path.Combine(_directory.FullName, "config.json"), _configuration.Upgrader);
            Notification.SetupAllProperties();
            Notification.Setup(notification => notification.Show(It.IsAny<NotificationDeliverOption>()))
                .Callback(() => IsAlive = true);
            Notification.Setup(notification => notification.Remove()).Callback(() => IsAlive = false);
            var profileEnv = Mock.Of<IProfileEnv>(env => env.GetPersistentDir() == _directory.FullName);
            Helper = new ProfileNotificationHelper(Notification.Object, new ProfileActionBuilder(null!, profileEnv), Config, Clock);
        }

        public Task NotifyAsync(CancellationToken token) => Helper.NotifyAsync(new TextProfile("test notification"), token);

        public void Dispose()
        {
            Helper.Dispose();
            _configuration.Dispose();
            _directory.Delete(true);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private TimeSpan _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            _now += elapsed;
            foreach (var timer in _timers.ToArray())
            {
                timer.FireIfDue(_now);
            }
        }

        private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
        {
            private TimeSpan? _due;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.AreEqual(Timeout.InfiniteTimeSpan, period);
                if (_disposed)
                {
                    return false;
                }
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                return true;
            }

            public void FireIfDue(TimeSpan now)
            {
                if (!_disposed && _due is { } due && due <= now)
                {
                    _due = null;
                    callback(state);
                }
            }

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
