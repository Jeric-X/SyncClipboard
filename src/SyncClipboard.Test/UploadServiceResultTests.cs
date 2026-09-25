using Moq;
using NativeNotification.Interface;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.UserServices.ClipboardService;
using SyncClipboard.Shared.Profiles;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test;

[TestClass]
[DoNotParallelize]
public class UploadServiceResultTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task ExhaustedRetries_ReturnFailureReasonAndPreserveCaches(bool timeout, bool notifyFailure)
    {
        var fixture = new Fixture();
        fixture.Server.Setup(server => server.SetProfileAsync(
            It.IsAny<Profile>(), It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(timeout ? new TaskCanceledException("timeout") : new IOException("remote write failed"));

        var result = await fixture.RunAsync(TestContext.CancellationTokenSource.Token, notifyFailure: notifyFailure);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(timeout ? SyncClipboard.Core.I18n.Strings.Timeout : "remote write failed", result.Reason);
        Assert.AreEqual(notifyFailure ? 1 : 0,
            fixture.Notifications.Invocations.Count(invocation => invocation.Method.Name == "Show"));
        fixture.VerifyWrites(2);
        fixture.AssertCachesUnchanged();
        Assert.AreEqual(1, fixture.StartCount);
        Assert.AreEqual(1, fixture.StopCount);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task SuccessfulUpload_ReturnsSuccessAndUpdatesCaches(bool alreadySynchronized, bool failFirstAttempt)
    {
        var fixture = new Fixture();
        if (alreadySynchronized)
        {
            fixture.Server.Setup(server => server.GetProfileAsync(It.IsAny<CancellationToken>())).ReturnsAsync(fixture.Profile);
        }
        if (failFirstAttempt)
        {
            fixture.Server.SetupSequence(server => server.SetProfileAsync(
                It.IsAny<Profile>(), It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("temporary failure"))
                .Returns(Task.CompletedTask);
        }

        var result = await fixture.RunAsync(TestContext.CancellationTokenSource.Token);

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.Reason);
        Assert.AreSame(fixture.Profile, fixture.UploadCache);
        Assert.AreSame(fixture.Profile, fixture.DownloadCache);
        fixture.VerifyWrites(alreadySynchronized ? 0 : failFirstAttempt ? 2 : 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_ThrowsWithoutRetryingOrUpdatingCaches(bool cancelBeforeStart)
    {
        var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
        if (cancelBeforeStart)
        {
            cancellation.Cancel();
        }
        else
        {
            fixture.Server.Setup(server => server.SetProfileAsync(
                It.IsAny<Profile>(), It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    cancellation.Cancel();
                    return Task.FromException(new OperationCanceledException(cancellation.Token));
                });
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.RunAsync(cancellation.Token));
        fixture.VerifyWrites(cancelBeforeStart ? 0 : 1);
        fixture.AssertCachesUnchanged();
        Assert.AreEqual(fixture.StartCount, fixture.StopCount);
    }

    [TestMethod]
    [DataRow("cut", "Skipped: Cutting operation detected.")]
    [DataRow("excluded", "Skipped: Sensitive content marked by system.")]
    [DataRow("unknown", "Skipped: Clipboard content type is not supported.")]
    public async Task SkippedUpload_ReturnsReasonWithoutContactingServer(string scenario, string expectedReason)
    {
        var fixture = new Fixture();
        fixture.Config.DoNotUploadWhenCut = true;
        var meta = new ClipboardMetaInfomation();
        if (scenario == "cut")
        {
            meta.Effects = DragDropEffects.Move;
        }
        else if (scenario == "excluded")
        {
            meta.ExcludeForSync = true;
        }
        else
        {
            fixture.Profile = new UnknownProfile();
        }

        var result = await fixture.RunAsync(TestContext.CancellationTokenSource.Token, scenario != "unknown", meta, notifyFailure: true);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(expectedReason, result.Reason);
        Assert.IsFalse(fixture.Notifications.Invocations.Any(invocation => invocation.Method.Name == "Show"));
        fixture.Server.Verify(server => server.GetProfileAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.AssertCachesUnchanged();
    }

    [TestMethod]
    [DataRow("success", true)]
    [DataRow("failure", true)]
    [DataRow("root", true)]
    [DataRow("failure", false)]
    public async Task ManualUpload_ShowsOnlyTheActualOutcomeAndHonorsNotificationSetting(string scenario, bool notify)
    {
        var fixture = new Fixture();
        fixture.Config.NotifyOnManualUpload = notify;
        if (scenario == "failure")
        {
            fixture.Server.Setup(server => server.SetProfileAsync(
                It.IsAny<Profile>(), It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("remote write failed"));
        }
        else if (scenario == "root")
        {
            fixture.Profile = new RootDirectoryProfileTests.NoContentReadGroupProfile([Path.GetPathRoot(Path.GetTempPath())!]);
        }

        await fixture.RunManualAsync();

        fixture.Notification.Verify(notification => notification.Show(It.IsAny<NotificationDeliverOption>()),
            notify ? Times.Once() : Times.Never());
        Assert.IsFalse(fixture.Notifications.Invocations.Any(invocation => invocation.Method.Name == "Show"),
            "The retry loop must not emit a second failure notification.");
        if (notify)
        {
            var expectedTitle = scenario switch
            {
                "success" => SyncClipboard.Core.I18n.Strings.Uploaded,
                _ => SyncClipboard.Core.I18n.Strings.ManualUploadFailed
            };
            var expectedMessage = scenario switch
            {
                "success" => fixture.Profile.ShortDisplayText,
                "root" => SyncClipboard.Core.I18n.Strings.RootDirectoryNotSupported,
                _ => "remote write failed"
            };
            Assert.AreEqual(expectedTitle, fixture.Notification.Object.Title);
            Assert.AreEqual(expectedMessage, fixture.Notification.Object.Message);
        }
    }

    [TestMethod]
    public async Task ManualUpload_CancellationDoesNotNotifyOrUpdateCaches()
    {
        var fixture = new Fixture();
        fixture.Server.Setup(server => server.SetProfileAsync(
            It.IsAny<Profile>(), It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                fixture.CancelUpload();
                return Task.FromException(new OperationCanceledException());
            });

        await fixture.RunManualAsync();

        fixture.Notification.Verify(notification => notification.Show(It.IsAny<NotificationDeliverOption>()), Times.Never);
        Assert.IsFalse(fixture.Notifications.Invocations.Any(invocation => invocation.Method.Name == "Show"));
        fixture.AssertCachesUnchanged();
        fixture.VerifyWrites(1);
    }

    private sealed class Fixture
    {
        private readonly UploadService _upload;
        private readonly DownloadService _download;
        private readonly Profile _previousProfile = new TextProfile("previously synchronized");

        public Profile Profile { get; set; } = new TextProfile("local clipboard");
        public SyncConfig Config { get; } = new() { RetryTimes = 1, IntervalTime = 0, NotifyOnManualUpload = true };
        public Mock<IRemoteClipboardServer> Server { get; } = new();
        public Mock<INotification> Notification { get; } = new();
        public Mock<INotificationManager> Notifications { get; } = new();
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public Profile? UploadCache => (Profile?)GetField(_upload, "_profileCache");
        public Profile? DownloadCache => (Profile?)GetField(_download, "_remoteProfileCache");

        public Fixture()
        {
            Server.Setup(server => server.GetProfileAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new TextProfile("remote clipboard"));
            Server.Setup(server => server.SetProfileAsync(
                It.IsAny<Profile>(), It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var clipboard = new Mock<IClipboardFactory>();
            clipboard.Setup(factory => factory.CreateProfileFromLocal(It.IsAny<CancellationToken>())).ReturnsAsync(() => Profile);
            Notification.SetupAllProperties();
            Notifications.SetupGet(manager => manager.Shared).Returns(Notification.Object);

            // Isolate the real upload/retry/notification flow from application startup and external accounts.
            var factory = (RemoteClipboardServerFactory)RuntimeHelpers.GetUninitializedObject(typeof(RemoteClipboardServerFactory));
            SetField(factory, "_current", Server.Object);
            SetField(factory, "_serverStateLock", new Lock());
            _download = (DownloadService)RuntimeHelpers.GetUninitializedObject(typeof(DownloadService));
            SetField(_download, "_remoteProfileCache", _previousProfile);
            _upload = (UploadService)RuntimeHelpers.GetUninitializedObject(typeof(UploadService));
            SetField(_upload, "_logger", Mock.Of<ILogger>());
            SetField(_upload, "_trayIcon", Mock.Of<ITrayIcon>());
            SetField(_upload, "_notificationManager", Notifications.Object);
            SetField(_upload, "_clipboardFactory", clipboard.Object);
            SetField(_upload, "_remoteClipboardServerFactory", factory);
            SetField(_upload, "_syncConfig", Config);
            SetField(_upload, "_profileCache", _previousProfile);
            SetField(_upload, "_cancelSourceLocker", new Lock(), typeof(ClipboardHander));
            typeof(UploadService).GetProperty("DownloadService", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(_upload, _download);
            _upload.PushStarted += () => StartCount++;
            _upload.PushStopped += () => StopCount++;
        }

        public Task<UploadResult> RunAsync(
            CancellationToken token, bool contentControl = false, ClipboardMetaInfomation? meta = null, bool notifyFailure = false)
            => (Task<UploadResult>)typeof(UploadService).GetMethod("CheckAndUpload", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_upload, [meta ?? new ClipboardMetaInfomation(), Profile, contentControl, token, notifyFailure])!;

        public void CancelUpload() => _upload.CancelProcess();

        public Task RunManualAsync()
            => (Task)typeof(UploadService).GetMethod("QuickUploadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_upload, [false, new ClipboardMetaInfomation(), Profile])!;

        public void VerifyWrites(int count)
            => Server.Verify(server => server.SetProfileAsync(
                It.IsAny<Profile>(), It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()), Times.Exactly(count));

        public void AssertCachesUnchanged()
        {
            Assert.AreSame(_previousProfile, UploadCache);
            Assert.AreSame(_previousProfile, DownloadCache);
        }

        private static void SetField<T>(object target, string name, T value, Type? declaringType = null)
            => (declaringType ?? target.GetType()).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(target, value);

        private static object? GetField(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    }
}
