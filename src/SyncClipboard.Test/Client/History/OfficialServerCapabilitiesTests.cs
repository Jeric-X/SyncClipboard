using Moq;
using NativeNotification.Interface;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class OfficialServerCapabilitiesTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConfigChanges_NotifyOncePerConfiguration(bool hasCapabilities)
    {
        using var handler = new CapabilityHandler { Capabilities = hasCapabilities ? "{\"historySync\":true}" : null };
        var notifications = new Mock<INotificationManager>();
        using var adapter = CreateAdapter(handler, notifications.Object);
        await adapter.NotifyCapabilitiesOnConfigChangedAsync();
        await adapter.NotifyCapabilitiesOnConfigChangedAsync();
        Assert.AreEqual(1, notifications.Invocations.Count(i => i.Method.Name == "Show"));
        adapter.SetConfig(new OfficialConfig { RemoteURL = "https://other.test" }, new SyncConfig());
        await adapter.NotifyCapabilitiesOnConfigChangedAsync();
        Assert.AreEqual(2, notifications.Invocations.Count(i => i.Method.Name == "Show"));
        Assert.AreEqual(hasCapabilities ? 0 : 2, handler.VersionRequests);
    }

    [TestMethod]
    public async Task FailedCapabilityProbe_DoesNotReportAnUnsupportedServer()
    {
        using var handler = new CapabilityHandler { FailProbe = true };
        var notifications = new Mock<INotificationManager>();
        using var adapter = CreateAdapter(handler, notifications.Object);
        await adapter.NotifyCapabilitiesOnConfigChangedAsync();
        Assert.IsEmpty(notifications.Invocations);
        handler.FailProbe = false;
        adapter.SetConfig(new OfficialConfig { RemoteURL = "https://other.test" }, new SyncConfig());
        await adapter.NotifyCapabilitiesOnConfigChangedAsync();
        Assert.AreEqual(1, notifications.Invocations.Count(i => i.Method.Name == "Show"));
    }

    [TestMethod]
    public async Task SupportedCapabilities_DoNotRequestOldVersionOrNotify()
    {
        using var handler = new CapabilityHandler
        {
            Capabilities = "{\"historySync\":true,\"historyTags\":true}",
            Version = "1.0.0"
        };
        var notifications = new Mock<INotificationManager>();
        using var adapter = CreateAdapter(handler, notifications.Object);
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://history.test/") };
        await OfficialAdapter.CheckHistorySupportAsync(client, TestContext.CancellationTokenSource.Token);
        await adapter.NotifyCapabilitiesOnConfigChangedAsync();
        Assert.AreEqual(0, handler.VersionRequests);
        Assert.IsEmpty(notifications.Invocations);
    }

    [TestMethod]
    [DataRow("{\"historySync\":false,\"historyTags\":true}")]
    [DataRow("{}")]
    public async Task HistorySyncUnsupported_RejectsConnectionWithoutVersionFallback(string json)
    {
        using var handler = new CapabilityHandler { Capabilities = json };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://history.test/") };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => OfficialAdapter.CheckHistorySupportAsync(client, TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(0, handler.VersionRequests);
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(500)]
    [DataRow(405)]
    public async Task CapabilityHttpFailure_DoesNotFallBackToVersion(int status)
    {
        using var handler = new CapabilityHandler { CapabilityStatus = (HttpStatusCode)status };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://history.test/") };
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => OfficialAdapter.CheckHistorySupportAsync(client, TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(0, handler.VersionRequests);
    }

    [TestMethod]
    public async Task NullCapabilities_DoesNotFallBackToVersion()
    {
        using var handler = new CapabilityHandler { Capabilities = "null" };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://history.test/") };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => OfficialAdapter.CheckHistorySupportAsync(client, TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(0, handler.VersionRequests);
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("{\"historySync\":\"true\",\"historyTags\":false}")]
    public async Task MalformedCapabilities_DoesNotFallBackToVersion(string json)
    {
        using var handler = new CapabilityHandler { Capabilities = json };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://history.test/") };
        await Assert.ThrowsAsync<JsonException>(() => OfficialAdapter.CheckHistorySupportAsync(client, TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(0, handler.VersionRequests);
    }

    [TestMethod]
    [DataRow("1.0.0")]
    [DataRow("invalid")]
    public async Task MissingCapabilities_RejectsOldOrInvalidVersion(string version)
    {
        using var handler = new CapabilityHandler { Version = version };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://history.test/") };
        if (version == "invalid")
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => OfficialAdapter.CheckHistorySupportAsync(client, TestContext.CancellationTokenSource.Token));
        else
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => OfficialAdapter.CheckHistorySupportAsync(client, TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(1, handler.VersionRequests);
    }

    private static OfficialAdapter CreateAdapter(HttpMessageHandler handler, INotificationManager? notifications = null)
    {
        var adapter = new OfficialAdapter(Mock.Of<ILogger>(), Mock.Of<IAppConfig>(), notifications);
        var field = typeof(OfficialAdapter).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)field.GetValue(adapter)!).Dispose();
        field.SetValue(adapter, new HttpClient(handler) { BaseAddress = new Uri("https://history.test/") });
        return adapter;
    }

    private sealed class CapabilityHandler : HttpMessageHandler
    {
        public int VersionRequests { get; private set; }
        public bool FailProbe { get; set; }
        public string? Capabilities { get; set; }
        public HttpStatusCode? CapabilityStatus { get; set; }
        public string Version { get; set; } = Env.RequestServerVersion;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/capabilities")
            {
                if (FailProbe)
                    throw new HttpRequestException("Network unavailable.");
                return Task.FromResult(new HttpResponseMessage(CapabilityStatus ?? (Capabilities is null ? HttpStatusCode.NotFound : HttpStatusCode.OK))
                {
                    Content = new StringContent(Capabilities ?? "")
                });
            }
            if (path == "/api/version")
            {
                VersionRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Version)
                });
            }
            throw new InvalidOperationException($"Unexpected request: {path}");
        }
    }
}
