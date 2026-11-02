using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Server.Core.Controllers;
using SyncClipboard.Shared;

namespace SyncClipboard.Test.Server;

[TestClass]
public class ServerCapabilitiesTests
{
    [TestMethod]
    public async Task Capabilities_ReturnsHistorySyncAndTagsAsJsonBooleans()
    {
        var controller = new SyncClipboardController(null!, null!, null!, null!);
        var result = controller.GetCapabilities().Result!;
        var (status, contentType, body) = await ExecuteAsync(result);

        Assert.AreEqual(StatusCodes.Status200OK, status);
        Assert.StartsWith("application/json", contentType);
        using var json = JsonDocument.Parse(body);
        Assert.IsTrue(json.RootElement.GetProperty("historySync").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("historyTags").GetBoolean());
    }

    [TestMethod]
    public async Task Version_PreservesVersionStringForExistingClients()
    {
        var controller = new SyncClipboardController(null!, null!, null!, null!);
        var (status, contentType, body) = await ExecuteAsync(controller.GetVersion());

        Assert.AreEqual(StatusCodes.Status200OK, status);
        Assert.StartsWith("text/plain", contentType);
        Assert.AreEqual(SyncClipboardProperty.AppVersion, body);
    }

    private static async Task<(int Status, string ContentType, string Body)> ExecuteAsync(IActionResult result)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        await using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        await using var output = new MemoryStream();
        context.Response.Body = output;
        await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));
        output.Position = 0;
        using var reader = new StreamReader(output);
        return (context.Response.StatusCode, context.Response.ContentType!, await reader.ReadToEndAsync());
    }
}
