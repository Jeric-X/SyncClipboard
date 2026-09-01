using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncClipboard.Server.Core.Controllers;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Test;

[TestClass]
public class CapabilitiesControllerTests
{
    [TestMethod]
    public void Get_ReturnsCurrentRealtimeTransportCapabilities()
    {
        var controller = new CapabilitiesController();

        var result = controller.Get();

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var capabilities = ok.Value as RealtimeCapabilitiesDto;
        Assert.IsNotNull(capabilities);
        Assert.IsTrue(capabilities.SignalR);
        Assert.IsFalse(capabilities.Push.Fcm);
    }

    [TestMethod]
    public void Contract_UsesAuthenticatedApiCapabilitiesRouteAndStableJsonNames()
    {
        var controllerType = typeof(CapabilitiesController);
        var route = controllerType.GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>()
            .Single();
        Assert.AreEqual("api/capabilities", route.Template);
        Assert.IsTrue(controllerType.IsDefined(typeof(AuthorizeAttribute), false));

        var getMethod = controllerType.GetMethod(nameof(CapabilitiesController.Get));
        Assert.IsNotNull(getMethod);
        Assert.IsTrue(getMethod.IsDefined(typeof(HttpGetAttribute), false));

        var json = JsonSerializer.Serialize(new RealtimeCapabilitiesDto
        {
            SignalR = true,
            Push = new PushCapabilitiesDto { Fcm = false }
        });
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.IsTrue(root.GetProperty("signalR").GetBoolean());
        Assert.IsFalse(root.GetProperty("push").GetProperty("fcm").GetBoolean());
    }
}
