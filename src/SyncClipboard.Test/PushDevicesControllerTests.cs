using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncClipboard.Server.Core.Controllers;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.PushDevices;

namespace SyncClipboard.Test;

[TestClass]
public class PushDevicesControllerTests
{
    [TestMethod]
    public async Task Put_NormalizesAndRegistersSupportedDevice()
    {
        var registry = new Mock<IPushDeviceRegistry>();
        var controller = new PushDevicesController(registry.Object);
        var deviceId = Guid.NewGuid();
        var request = new PushDeviceRegistrationRequest
        {
            Platform = "ANDROID",
            Provider = "FCM",
            Token = " push-token ",
            AppVersion = " 1.2.3 "
        };

        var result = await controller.Put(deviceId.ToString("B"), request, CancellationToken.None);

        Assert.IsInstanceOfType<NoContentResult>(result);
        registry.Verify(value => value.UpsertAsync(
            deviceId.ToString("D"),
            "android",
            "fcm",
            "push-token",
            "1.2.3",
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task Put_RejectsInvalidRegistrationWithoutWriting()
    {
        var registry = new Mock<IPushDeviceRegistry>();
        var controller = new PushDevicesController(registry.Object);
        var request = new PushDeviceRegistrationRequest
        {
            Platform = "android",
            Provider = "apns",
            Token = "push-token"
        };

        var result = await controller.Put(Guid.NewGuid().ToString(), request, CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        registry.Verify(value => value.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Put_RejectsInvalidDeviceIdWithoutWriting()
    {
        var registry = new Mock<IPushDeviceRegistry>();
        var controller = new PushDevicesController(registry.Object);
        var request = new PushDeviceRegistrationRequest
        {
            Platform = "android",
            Provider = "fcm",
            Token = "push-token"
        };

        var result = await controller.Put("not-a-uuid", request, CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        registry.Verify(value => value.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Delete_IsIdempotentAndNormalizesDeviceId()
    {
        var registry = new Mock<IPushDeviceRegistry>();
        registry.Setup(value => value.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var controller = new PushDevicesController(registry.Object);
        var deviceId = Guid.NewGuid();

        var result = await controller.Delete(deviceId.ToString("B"), CancellationToken.None);

        Assert.IsInstanceOfType<NoContentResult>(result);
        registry.Verify(value => value.RemoveAsync(
            deviceId.ToString("D"), CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public void Contract_UsesAuthenticatedPushRegistrationRoute()
    {
        var controllerType = typeof(PushDevicesController);
        var route = controllerType.GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>()
            .Single();

        Assert.AreEqual("api/devices/{deviceId}/push", route.Template);
        Assert.IsTrue(controllerType.IsDefined(typeof(AuthorizeAttribute), false));
        Assert.IsTrue(controllerType.GetMethod(nameof(PushDevicesController.Put))!
            .IsDefined(typeof(HttpPutAttribute), false));
        Assert.IsTrue(controllerType.GetMethod(nameof(PushDevicesController.Delete))!
            .IsDefined(typeof(HttpDeleteAttribute), false));
    }
}
