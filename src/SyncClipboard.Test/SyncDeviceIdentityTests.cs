using Microsoft.AspNetCore.Mvc;
using SyncClipboard.Server.Core.Controllers;
using SyncClipboard.Server.Core.Services.PushDevices;

namespace SyncClipboard.Test;

[TestClass]
public class SyncDeviceIdentityTests
{
    [TestMethod]
    public void Normalize_ValidUuid_ReturnsCanonicalDeviceId()
    {
        var deviceId = Guid.NewGuid();

        var normalized = SyncDeviceIdentity.Normalize(deviceId.ToString("B").ToUpperInvariant());

        Assert.AreEqual(deviceId.ToString("D"), normalized);
    }

    [TestMethod]
    public void Normalize_InvalidOrMissingUuid_ReturnsNull()
    {
        Assert.IsNull(SyncDeviceIdentity.Normalize(null));
        Assert.IsNull(SyncDeviceIdentity.Normalize("not-a-uuid"));
    }

    [TestMethod]
    public void PutSyncProfile_BindsOriginFromStableDeviceIdHeader()
    {
        var parameter = typeof(SyncClipboardController)
            .GetMethod(nameof(SyncClipboardController.PutSyncProfile))!
            .GetParameters()
            .Single(value => value.Name == "originDeviceId");
        var attribute = parameter
            .GetCustomAttributes(typeof(FromHeaderAttribute), false)
            .Cast<FromHeaderAttribute>()
            .Single();

        Assert.AreEqual(SyncDeviceIdentity.HeaderName, attribute.Name);
        Assert.AreEqual("X-SyncClipboard-Device-Id", attribute.Name);
    }
}
