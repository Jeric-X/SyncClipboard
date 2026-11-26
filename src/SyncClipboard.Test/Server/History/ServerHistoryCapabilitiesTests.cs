using Microsoft.AspNetCore.Mvc;
using SyncClipboard.Server.Core.Controllers;
using System.Text.Json;

namespace SyncClipboard.Test.Server.History;

[TestClass]
public class ServerHistoryCapabilitiesTests
{
    [TestMethod]
    public void GetCapabilities_AdvertisesHistorySyncTagsAndTitle()
    {
        var controller = new SyncClipboardController(null!, null!, null!);
        var ok = controller.GetCapabilities().Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var json = JsonSerializer.SerializeToElement(ok.Value, JsonSerializerOptions.Web);

        Assert.IsTrue(json.GetProperty("historySync").GetBoolean());
        Assert.IsTrue(json.GetProperty("historyTags").GetBoolean());
        Assert.IsTrue(json.GetProperty("historyTitle").GetBoolean());
    }
}
