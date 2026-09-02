using Microsoft.Extensions.Logging;
using Moq;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.Notifications.Fcm;
using SyncClipboard.Server.Core.Services.PushDevices;

namespace SyncClipboard.Test;

[TestClass]
public class FcmProfileChangeDeliveryTests
{
    [TestMethod]
    public async Task DeliverAsync_SendsHashOnlyHintToEveryRegisteredDevice()
    {
        var registrations = new List<PushDeviceRegistration>
        {
            CreateRegistration("device-a", "token-a"),
            CreateRegistration("device-b", "token-b")
        };
        var registry = CreateRegistry(registrations);
        var fcmClient = CreateAvailableClient();
        var delivery = CreateDelivery(registry, fcmClient);

        await delivery.DeliverAsync(
            new FcmProfileChangeHint("profile-hash", null),
            CancellationToken.None);

        fcmClient.Verify(value => value.SendProfileChangedAsync(
            "token-a", "profile-hash", CancellationToken.None), Times.Once);
        fcmClient.Verify(value => value.SendProfileChangedAsync(
            "token-b", "profile-hash", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task DeliverAsync_ExcludesOriginatingDeviceOnly()
    {
        var originDeviceId = Guid.NewGuid().ToString("D");
        var registrations = new List<PushDeviceRegistration>
        {
            CreateRegistration(originDeviceId, "origin-token"),
            CreateRegistration(Guid.NewGuid().ToString("D"), "other-token")
        };
        var registry = CreateRegistry(registrations);
        var fcmClient = CreateAvailableClient();
        var delivery = CreateDelivery(registry, fcmClient);

        await delivery.DeliverAsync(
            new FcmProfileChangeHint("profile-hash", originDeviceId.ToUpperInvariant()),
            CancellationToken.None);

        fcmClient.Verify(value => value.SendProfileChangedAsync(
            "origin-token", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fcmClient.Verify(value => value.SendProfileChangedAsync(
            "other-token", "profile-hash", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task DeliverAsync_WhenUnavailable_DoesNotReadRegistry()
    {
        var registry = new Mock<IPushDeviceRegistry>();
        var fcmClient = new Mock<IFcmPushClient>();
        fcmClient.SetupGet(value => value.IsAvailable).Returns(false);
        var delivery = CreateDelivery(registry, fcmClient);

        await delivery.DeliverAsync(
            new FcmProfileChangeHint("profile-hash", null),
            CancellationToken.None);

        registry.Verify(value => value.GetByProviderAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeliverAsync_WhenOneSendFails_ContinuesOtherDevices()
    {
        var registrations = new List<PushDeviceRegistration>
        {
            CreateRegistration("device-a", "token-a"),
            CreateRegistration("device-b", "token-b")
        };
        var registry = CreateRegistry(registrations);
        var fcmClient = CreateAvailableClient();
        fcmClient.Setup(value => value.SendProfileChangedAsync(
                "token-a", "profile-hash", CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("send failed"));
        var delivery = CreateDelivery(registry, fcmClient);

        await delivery.DeliverAsync(
            new FcmProfileChangeHint("profile-hash", null),
            CancellationToken.None);

        fcmClient.Verify(value => value.SendProfileChangedAsync(
            "token-b", "profile-hash", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task DeliverAsync_WhenRegistryFails_DoesNotSend()
    {
        var registry = new Mock<IPushDeviceRegistry>();
        registry.Setup(value => value.GetByProviderAsync("fcm", CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("registry failed"));
        var fcmClient = CreateAvailableClient();
        var delivery = CreateDelivery(registry, fcmClient);

        await delivery.DeliverAsync(
            new FcmProfileChangeHint("profile-hash", null),
            CancellationToken.None);

        fcmClient.Verify(value => value.SendProfileChangedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static FcmProfileChangeDelivery CreateDelivery(
        Mock<IPushDeviceRegistry> registry,
        Mock<IFcmPushClient> fcmClient)
    {
        return new FcmProfileChangeDelivery(
            registry.Object,
            fcmClient.Object,
            Mock.Of<ILogger<FcmProfileChangeDelivery>>());
    }

    private static Mock<IPushDeviceRegistry> CreateRegistry(
        IReadOnlyList<PushDeviceRegistration> registrations)
    {
        var registry = new Mock<IPushDeviceRegistry>();
        registry.Setup(value => value.GetByProviderAsync("fcm", CancellationToken.None))
            .ReturnsAsync(registrations);
        return registry;
    }

    private static Mock<IFcmPushClient> CreateAvailableClient()
    {
        var fcmClient = new Mock<IFcmPushClient>();
        fcmClient.SetupGet(value => value.IsAvailable).Returns(true);
        return fcmClient;
    }

    private static PushDeviceRegistration CreateRegistration(string deviceId, string pushToken)
    {
        return new PushDeviceRegistration(
            deviceId, "android", "fcm", pushToken, "1.0.0", DateTimeOffset.UtcNow);
    }
}
