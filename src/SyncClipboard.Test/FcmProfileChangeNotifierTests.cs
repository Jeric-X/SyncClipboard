using Moq;
using Microsoft.Extensions.Logging;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.Notifications;
using SyncClipboard.Server.Core.Services.Notifications.Fcm;
using SyncClipboard.Server.Core.Services.PushDevices;
using SyncClipboard.Shared;

namespace SyncClipboard.Test;

[TestClass]
public class FcmProfileChangeNotifierTests
{
    [TestMethod]
    public async Task NotifyProfileChanged_SendsHashOnlyHintToEveryRegisteredDevice()
    {
        var registrations = new List<PushDeviceRegistration>
        {
            CreateRegistration("device-a", "token-a"),
            CreateRegistration("device-b", "token-b")
        };
        var registry = new Mock<IPushDeviceRegistry>();
        registry.Setup(value => value.GetByProviderAsync("fcm", CancellationToken.None))
            .ReturnsAsync(registrations);
        var fcmClient = new Mock<IFcmPushClient>();
        fcmClient.SetupGet(value => value.IsAvailable).Returns(true);
        var notifier = new FcmProfileChangeNotifier(
            registry.Object, fcmClient.Object, Mock.Of<ILogger<FcmProfileChangeNotifier>>());
        var profile = new ProfileDto
        {
            Hash = "profile-hash",
            Text = "must-not-enter-push"
        };

        await notifier.NotifyProfileChanged(
            new ProfileChangeNotification(profile), CancellationToken.None);

        fcmClient.Verify(value => value.SendProfileChangedAsync(
            "token-a", "profile-hash", CancellationToken.None), Times.Once);
        fcmClient.Verify(value => value.SendProfileChangedAsync(
            "token-b", "profile-hash", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task NotifyProfileChanged_ExcludesOriginatingDeviceOnly()
    {
        var originDeviceId = Guid.NewGuid().ToString("D");
        var registrations = new List<PushDeviceRegistration>
        {
            CreateRegistration(originDeviceId, "origin-token"),
            CreateRegistration(Guid.NewGuid().ToString("D"), "other-token")
        };
        var registry = new Mock<IPushDeviceRegistry>();
        registry.Setup(value => value.GetByProviderAsync("fcm", CancellationToken.None))
            .ReturnsAsync(registrations);
        var fcmClient = new Mock<IFcmPushClient>();
        fcmClient.SetupGet(value => value.IsAvailable).Returns(true);
        var notifier = new FcmProfileChangeNotifier(
            registry.Object, fcmClient.Object, Mock.Of<ILogger<FcmProfileChangeNotifier>>());

        await notifier.NotifyProfileChanged(
            new ProfileChangeNotification(
                new ProfileDto { Hash = "profile-hash" },
                originDeviceId.ToUpperInvariant()),
            CancellationToken.None);

        fcmClient.Verify(value => value.SendProfileChangedAsync(
            "origin-token", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fcmClient.Verify(value => value.SendProfileChangedAsync(
            "other-token", "profile-hash", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task NotifyProfileChanged_WhenUnavailable_DoesNotReadRegistry()
    {
        var registry = new Mock<IPushDeviceRegistry>();
        var fcmClient = new Mock<IFcmPushClient>();
        fcmClient.SetupGet(value => value.IsAvailable).Returns(false);
        var notifier = new FcmProfileChangeNotifier(
            registry.Object, fcmClient.Object, Mock.Of<ILogger<FcmProfileChangeNotifier>>());

        await notifier.NotifyProfileChanged(
            new ProfileChangeNotification(new ProfileDto { Hash = "profile-hash" }),
            CancellationToken.None);

        registry.Verify(value => value.GetByProviderAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task NotifyProfileChanged_WhenOneSendFails_ContinuesOtherDevices()
    {
        var registrations = new List<PushDeviceRegistration>
        {
            CreateRegistration("device-a", "token-a"),
            CreateRegistration("device-b", "token-b")
        };
        var registry = new Mock<IPushDeviceRegistry>();
        registry.Setup(value => value.GetByProviderAsync("fcm", CancellationToken.None))
            .ReturnsAsync(registrations);
        var fcmClient = new Mock<IFcmPushClient>();
        fcmClient.SetupGet(value => value.IsAvailable).Returns(true);
        fcmClient.Setup(value => value.SendProfileChangedAsync(
                "token-a", "profile-hash", CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("send failed"));
        var notifier = new FcmProfileChangeNotifier(
            registry.Object, fcmClient.Object, Mock.Of<ILogger<FcmProfileChangeNotifier>>());

        await notifier.NotifyProfileChanged(
            new ProfileChangeNotification(new ProfileDto { Hash = "profile-hash" }),
            CancellationToken.None);

        fcmClient.Verify(value => value.SendProfileChangedAsync(
            "token-b", "profile-hash", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task NotifyProfileChanged_WhenRegistryFails_DoesNotFailProfileUpdate()
    {
        var registry = new Mock<IPushDeviceRegistry>();
        registry.Setup(value => value.GetByProviderAsync("fcm", CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("registry failed"));
        var fcmClient = new Mock<IFcmPushClient>();
        fcmClient.SetupGet(value => value.IsAvailable).Returns(true);
        var notifier = new FcmProfileChangeNotifier(
            registry.Object, fcmClient.Object, Mock.Of<ILogger<FcmProfileChangeNotifier>>());

        await notifier.NotifyProfileChanged(
            new ProfileChangeNotification(new ProfileDto { Hash = "profile-hash" }),
            CancellationToken.None);

        fcmClient.Verify(value => value.SendProfileChangedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PushDeviceRegistration CreateRegistration(string deviceId, string pushToken)
    {
        return new PushDeviceRegistration(
            deviceId, "android", "fcm", pushToken, "1.0.0", DateTimeOffset.UtcNow);
    }
}
