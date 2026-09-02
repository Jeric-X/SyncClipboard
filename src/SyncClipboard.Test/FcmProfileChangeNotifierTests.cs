using Microsoft.Extensions.Logging;
using Moq;
using SyncClipboard.Server.Core.Services.Notifications;
using SyncClipboard.Server.Core.Services.Notifications.Fcm;
using SyncClipboard.Shared;

namespace SyncClipboard.Test;

[TestClass]
public class FcmProfileChangeNotifierTests
{
    [TestMethod]
    public async Task NotifyProfileChanged_QueuesHashAndOriginWithoutWaitingForDelivery()
    {
        var queue = new Mock<IFcmProfileChangeQueue>();
        queue.Setup(value => value.TryEnqueue(It.IsAny<FcmProfileChangeHint>()))
            .Returns(true);
        var fcmClient = new Mock<IFcmPushClient>();
        fcmClient.SetupGet(value => value.IsAvailable).Returns(true);
        var notifier = new FcmProfileChangeNotifier(
            queue.Object,
            fcmClient.Object,
            Mock.Of<ILogger<FcmProfileChangeNotifier>>());
        var notification = new ProfileChangeNotification(
            new ProfileDto
            {
                Hash = "profile-hash",
                Text = "must-not-enter-push"
            },
            "origin-device");

        var notifyTask = notifier.NotifyProfileChanged(notification, CancellationToken.None);

        Assert.IsTrue(notifyTask.IsCompletedSuccessfully);
        await notifyTask;
        queue.Verify(value => value.TryEnqueue(
            new FcmProfileChangeHint("profile-hash", "origin-device")), Times.Once);
        fcmClient.Verify(value => value.SendProfileChangedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task NotifyProfileChanged_WhenUnavailable_DoesNotQueue()
    {
        var queue = new Mock<IFcmProfileChangeQueue>();
        var fcmClient = new Mock<IFcmPushClient>();
        fcmClient.SetupGet(value => value.IsAvailable).Returns(false);
        var notifier = new FcmProfileChangeNotifier(
            queue.Object,
            fcmClient.Object,
            Mock.Of<ILogger<FcmProfileChangeNotifier>>());

        await notifier.NotifyProfileChanged(
            new ProfileChangeNotification(new ProfileDto { Hash = "profile-hash" }),
            CancellationToken.None);

        queue.Verify(value => value.TryEnqueue(
            It.IsAny<FcmProfileChangeHint>()), Times.Never);
    }

    [TestMethod]
    public async Task Queue_WhenFull_KeepsLatestProfileChange()
    {
        var queue = new FcmProfileChangeQueue();
        Assert.IsTrue(queue.TryEnqueue(new FcmProfileChangeHint("hash-a", null)));
        Assert.IsTrue(queue.TryEnqueue(new FcmProfileChangeHint("hash-b", "device-b")));
        Assert.IsTrue(queue.TryEnqueue(new FcmProfileChangeHint("hash-c", "device-c")));

        var hint = await queue.DequeueAsync(CancellationToken.None);

        Assert.AreEqual(new FcmProfileChangeHint("hash-c", "device-c"), hint);
    }
}
