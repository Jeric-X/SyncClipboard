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
    public async Task MissingDurationUsesTenSeconds()
    {
        using var fixture = new Fixture();
        await fixture.NotifyAsync(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual("test notification", fixture.Notification.Object.Message);
        Assert.IsNotNull(fixture.Notification.Object.ContentAction);
        Assert.IsNotEmpty(fixture.Notification.Object.Buttons);

        Assert.AreEqual(TimeSpan.FromSeconds(10), fixture.Duration);
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

        Assert.AreEqual(seconds > 0 ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null, fixture.Duration);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(10)]
    public async Task NewNotificationUsesLatestDuration(int replacementDuration)
    {
        using var fixture = new Fixture();
        var token = TestContext.CancellationTokenSource.Token;
        await fixture.NotifyAsync(token);
        Assert.AreEqual(TimeSpan.FromSeconds(10), fixture.Duration);
        fixture.Config.SetConfig(new SyncConfig { NotificationDurationSeconds = replacementDuration });
        await fixture.NotifyAsync(token);

        Assert.AreEqual(replacementDuration > 0 ? TimeSpan.FromSeconds(replacementDuration) : (TimeSpan?)null,
            fixture.Duration);
        fixture.Notification.Verify(notification => notification.Show(It.IsAny<NotificationDeliverOption>()), Times.Exactly(2));
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

        Assert.AreEqual("new content", fixture.Notification.Object.Message);
        fixture.Notification.Verify(notification => notification.Show(It.IsAny<NotificationDeliverOption>()), Times.Once);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();
        private readonly ConfigurationTestServices _configuration = new();
        public Mock<INotification> Notification { get; } = new();
        public ConfigManager Config { get; }
        public ProfileNotificationHelper Helper { get; }
        public TimeSpan? Duration { get; private set; }

        public Fixture()
        {
            Config = new ConfigManager(Path.Combine(_directory.FullName, "config.json"), _configuration.Upgrader);
            Notification.SetupAllProperties();
            Notification.Setup(notification => notification.Show(It.IsAny<NotificationDeliverOption>()))
                .Callback<NotificationDeliverOption>(option => Duration = option.Duration);
            var profileEnv = Mock.Of<IProfileEnv>(env => env.GetPersistentDir() == _directory.FullName);
            Helper = new ProfileNotificationHelper(Notification.Object, new ProfileActionBuilder(null!, profileEnv), Config);
        }

        public Task NotifyAsync(CancellationToken token) => Helper.NotifyAsync(new TextProfile("test notification"), token);

        public void Dispose()
        {
            _configuration.Dispose();
            _directory.Delete(true);
        }
    }
}
