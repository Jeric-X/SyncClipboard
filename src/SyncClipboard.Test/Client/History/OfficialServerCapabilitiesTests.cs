using Moq;
using NativeNotification.Interface;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Shared.Profiles;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class OfficialServerCapabilitiesTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] ExpectedLocalTags = ["local"];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnsupportedTags_SyncsMetadataWithoutSendingOrOverwritingLocalTags(bool hasCapabilities)
    {
        using var handler = new LegacyHandler { Capabilities = hasCapabilities ? "{\"historySync\":true,\"historyTags\":false}" : null };
        var notifications = new Mock<INotificationManager>();
        using var adapter = CreateAdapter(handler, notifications.Object);
        var token = TestContext.CancellationTokenSource.Token;
        var local = new HistoryRecord { Type = ProfileType.Text, Hash = "record", Tags = ["local"] };
        var upload = local.ToHistoryRecordDto();
        var accepted = await adapter.UploadHistoryAsync(upload, null, cancellationToken: token);
        Assert.IsNull(accepted.Tags);
        local.ApplyChangesFromRemote(accepted);
        CollectionAssert.AreEqual(ExpectedLocalTags, local.Tags);
        Assert.IsTrue(local.Stared);
        CollectionAssert.AreEqual(ExpectedLocalTags, upload.Tags!);

        var update = new HistoryRecordUpdateDto { Tags = ["local"], Starred = true, Version = 8 };
        await adapter.UpdateHistoryAsync(ProfileType.Text, "record", update, token);
        CollectionAssert.AreEqual(ExpectedLocalTags, update.Tags!);
        var records = await adapter.GetHistoryAsync(token: token);
        Assert.IsNull(records.Single().Tags);
        var record = await adapter.GetHistoryByProfileIdAsync("Text-record", token);
        Assert.IsNotNull(record);
        Assert.IsNull(record.Tags);
        handler.Conflict = true;
        var conflict = await Assert.ThrowsExactlyAsync<RemoteHistoryConflictException>(
            () => adapter.UpdateHistoryAsync(ProfileType.Text, "record", update, token));
        Assert.IsNotNull(conflict.ServerRecord);
        local.ApplyFromServerUpdateDto(conflict.ServerRecord);
        CollectionAssert.AreEqual(ExpectedLocalTags, local.Tags);
        Assert.IsNull(conflict.ServerRecord.Tags);
        var uploadConflict = await Assert.ThrowsExactlyAsync<RemoteHistoryConflictException>(
            () => adapter.UploadHistoryAsync(upload, null, cancellationToken: token));
        Assert.IsNull(uploadConflict.ServerRecord!.Tags);
        Assert.AreEqual(hasCapabilities ? 0 : 1, handler.VersionRequests);
        Assert.AreEqual(1, notifications.Invocations.Count(i => i.Method.Name == "Show"));
    }

    [TestMethod]
    public async Task HistoryRequests_NotifyOnceAndRecheckAfterServerChange()
    {
        using var handler = new LegacyHandler();
        var notifications = new Mock<INotificationManager>();
        using var adapter = CreateAdapter(handler, notifications.Object);
        await adapter.GetHistoryAsync(token: TestContext.CancellationTokenSource.Token);
        await adapter.GetHistoryAsync(token: TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(1, notifications.Invocations.Count(i => i.Method.Name == "Show"));
        adapter.SetConfig(new OfficialConfig { RemoteURL = "https://other.test" }, new SyncConfig());
        await adapter.GetHistoryAsync(token: TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(2, notifications.Invocations.Count(i => i.Method.Name == "Show"));
        Assert.AreEqual(2, handler.VersionRequests);
    }

    [TestMethod]
    public async Task FailedCapabilityProbe_DoesNotReportAnUnsupportedServer()
    {
        using var handler = new LegacyHandler { FailProbe = true };
        var notifications = new Mock<INotificationManager>();
        using var adapter = CreateAdapter(handler, notifications.Object);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            adapter.GetHistoryAsync(token: TestContext.CancellationTokenSource.Token));
        Assert.IsEmpty(notifications.Invocations);
        handler.FailProbe = false;
        await adapter.GetHistoryAsync(token: TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(1, notifications.Invocations.Count(i => i.Method.Name == "Show"));
    }

    [TestMethod]
    public async Task SupportedCapabilities_IgnoreOldVersionAndTransmitTags()
    {
        using var handler = new LegacyHandler
        {
            Capabilities = "{\"historySync\":true,\"historyTags\":true}",
            Version = "1.0.0",
            ExpectTags = true
        };
        var notifications = new Mock<INotificationManager>();
        using var adapter = CreateAdapter(handler, notifications.Object);
        var token = TestContext.CancellationTokenSource.Token;
        var accepted = await adapter.UploadHistoryAsync(new HistoryRecordDto { Tags = ["local"] }, null, cancellationToken: token);
        Assert.IsNotNull(accepted.Tags);
        Assert.IsEmpty(accepted.Tags);
        await adapter.UpdateHistoryAsync(ProfileType.Text, "record", new HistoryRecordUpdateDto { Tags = ["local"], Starred = true }, token);
        Assert.AreEqual(0, handler.VersionRequests);
        Assert.IsEmpty(notifications.Invocations);
    }

    [TestMethod]
    public async Task HistorySyncUnsupported_RejectsHistoryWithoutVersionFallback()
    {
        using var handler = new LegacyHandler { Capabilities = "{\"historySync\":false,\"historyTags\":true}" };
        var notifications = new Mock<INotificationManager>();
        using var adapter = CreateAdapter(handler, notifications.Object);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => adapter.GetHistoryAsync(token: TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(0, handler.VersionRequests);
        Assert.AreEqual(0, handler.HistoryRequests);
        Assert.IsEmpty(notifications.Invocations);
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(500)]
    [DataRow(405)]
    public async Task CapabilityHttpFailure_DoesNotFallBackOrNotify(int status)
    {
        using var handler = new LegacyHandler { CapabilityStatus = (HttpStatusCode)status };
        var notifications = new Mock<INotificationManager>();
        using var adapter = CreateAdapter(handler, notifications.Object);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => adapter.GetHistoryAsync(token: TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(0, handler.VersionRequests);
        Assert.AreEqual(0, handler.HistoryRequests);
        Assert.IsEmpty(notifications.Invocations);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("{\"historySync\":true}")]
    [DataRow("{\"historySync\":\"true\",\"historyTags\":false}")]
    public async Task InvalidCapabilities_DoesNotFallBackOrNotify(string json)
    {
        using var handler = new LegacyHandler { Capabilities = json };
        var notifications = new Mock<INotificationManager>();
        using var adapter = CreateAdapter(handler, notifications.Object);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => adapter.GetHistoryAsync(token: TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(0, handler.VersionRequests);
        Assert.AreEqual(0, handler.HistoryRequests);
        Assert.IsEmpty(notifications.Invocations);
    }

    [TestMethod]
    public async Task MalformedCapabilities_DoesNotFallBackToVersion()
    {
        using var handler = new LegacyHandler { Capabilities = "not json" };
        using var adapter = CreateAdapter(handler);
        await Assert.ThrowsAsync<JsonException>(() => adapter.GetHistoryAsync(token: TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(0, handler.VersionRequests);
        Assert.AreEqual(0, handler.HistoryRequests);
    }

    [TestMethod]
    [DataRow("1.0.0")]
    [DataRow("invalid")]
    public async Task MissingCapabilities_RejectsOldOrInvalidVersion(string version)
    {
        using var handler = new LegacyHandler { Version = version };
        using var adapter = CreateAdapter(handler);
        if (version == "invalid")
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => adapter.GetHistoryAsync(token: TestContext.CancellationTokenSource.Token));
        else
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => adapter.GetHistoryAsync(token: TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(1, handler.VersionRequests);
        Assert.AreEqual(0, handler.HistoryRequests);
    }

    private static OfficialAdapter CreateAdapter(HttpMessageHandler handler, INotificationManager? notifications = null)
    {
        var adapter = new OfficialAdapter(Mock.Of<ILogger>(), Mock.Of<IAppConfig>(), notifications);
        var field = typeof(OfficialAdapter).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)field.GetValue(adapter)!).Dispose();
        field.SetValue(adapter, new HttpClient(handler) { BaseAddress = new Uri("https://history.test/") });
        return adapter;
    }

    private sealed class LegacyHandler : HttpMessageHandler
    {
        public int VersionRequests { get; private set; }
        public bool Conflict { get; set; }
        public bool FailProbe { get; set; }
        public string? Capabilities { get; set; }
        public HttpStatusCode? CapabilityStatus { get; set; }
        public string Version { get; set; } = Env.RequestServerVersion;
        public bool ExpectTags { get; set; }
        public int HistoryRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/capabilities")
            {
                if (FailProbe)
                    throw new HttpRequestException("Network unavailable.");
                return new HttpResponseMessage(CapabilityStatus ?? (Capabilities is null ? HttpStatusCode.NotFound : HttpStatusCode.OK))
                {
                    Content = new StringContent(Capabilities ?? "")
                };
            }
            if (path == "/api/version")
            {
                VersionRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Version)
                };
            }
            HistoryRequests++;
            if (request.Method == HttpMethod.Patch)
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.AreEqual(ExpectTags, payload.RootElement.TryGetProperty("tags", out _));
                Assert.IsTrue(payload.RootElement.GetProperty("starred").GetBoolean());
            }
            if (path == "/api/history")
            {
                var parts = (MultipartFormDataContent)request.Content!;
                Assert.AreEqual(ExpectTags, parts.Any(p => p.Headers.ContentDisposition!.Name!.Trim('"') == "Tags"));
            }
            if (Conflict)
                return new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new HistoryRecordUpdateDto { Tags = [], Starred = false, Version = 10 })
                };
            var record = new HistoryRecordDto { Type = ProfileType.Text, Hash = "record", Tags = [], Starred = true, Version = 9 };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = path == "/api/history/query" ? JsonContent.Create(new[] { record }) : JsonContent.Create(record)
            };
        }
    }
}
