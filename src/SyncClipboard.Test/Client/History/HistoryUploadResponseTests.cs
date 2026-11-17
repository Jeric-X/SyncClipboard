using Moq;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using SyncClipboard.Server.Core.Models;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryUploadResponseTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UploadHistoryAsync_ReturnsAcceptedRecordOrRejectsMissingResponse(bool missingRecord)
    {
        var accepted = new HistoryRecordDto { Hash = "hash", Version = 21 };
        using var handler = new UploadHandler(missingRecord ? null : accepted);
        using var adapter = CreateAdapter(handler);
        var incoming = new HistoryRecordDto { Hash = accepted.Hash, Version = 8 };
        var token = TestContext.CancellationTokenSource.Token;

        if (missingRecord)
        {
            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => adapter.UploadHistoryAsync(incoming, null, cancellationToken: token));
            return;
        }

        var result = await adapter.UploadHistoryAsync(incoming, null, cancellationToken: token);

        Assert.AreEqual(21, result.Version);
        Assert.AreEqual(accepted.Hash, result.Hash);
        Assert.AreEqual(8, incoming.Version);
    }

    [TestMethod]
    [DataRow(true, true, null, null)]
    [DataRow(true, true, "[]", "")]
    [DataRow(true, true, "[\"Work\",\"工作\"]", "null")]
    [DataRow(true, false, "[]", "本地名称")]
    [DataRow(false, true, "[]", "名称")]
    [DataRow(false, false, "[]", "本地名称")]
    public async Task Writes_SendKnownMetadataAccordingToIndependentCapabilities(
        bool supportsTags, bool supportsName, string? tagsJson, string? name)
    {
        var tags = tagsJson is null ? null : JsonSerializer.Deserialize<string[]>(tagsJson);
        var local = new HistoryRecord { Hash = "hash", Tags = ["local"] };
        using var handler = new MetadataHandler(supportsTags, supportsName);
        using var adapter = CreateAdapter(handler);
        var token = TestContext.CancellationTokenSource.Token;

        var incoming = new HistoryRecordDto { Hash = local.Hash, Tags = tags, Name = name };
        var accepted = await adapter.UploadHistoryAsync(incoming, null, cancellationToken: token);
        local.ApplyChangesFromRemote(accepted);
        Assert.AreEqual(supportsName ? name : null, handler.UploadName);
        Assert.AreEqual(supportsTags && tags is not null, handler.UploadTags is not null);
        if (handler.UploadTags is not null)
            CollectionAssert.AreEqual(tags, JsonSerializer.Deserialize<string[]>(handler.UploadTags));
        string[] expectedTags = supportsTags ? tags ?? ["server"] : ["local"];
        CollectionAssert.AreEqual(expectedTags, local.Tags);
        Assert.AreEqual(supportsName ? name ?? "server" : null, accepted.Name);

        await adapter.UpdateHistoryAsync(incoming.Type, incoming.Hash, incoming.ToUpdateDto(), token);
        Assert.AreEqual(supportsName && name is not null, handler.Patch.TryGetProperty("name", out var patchName));
        if (patchName.ValueKind != JsonValueKind.Undefined)
            Assert.AreEqual(name, patchName.GetString());
        Assert.AreEqual(supportsTags && tags is not null, handler.Patch.TryGetProperty("tags", out var patchTags));
        if (patchTags.ValueKind != JsonValueKind.Undefined)
            CollectionAssert.AreEqual(tags, patchTags.Deserialize<string[]>());
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task UploadConflict_ExposesOnlyAdvertisedMetadata(bool supportsTags, bool supportsName)
    {
        using var handler = new MetadataHandler(supportsTags, supportsName) { Conflict = true };
        using var adapter = CreateAdapter(handler);
        var token = TestContext.CancellationTokenSource.Token;
        var conflict = await Assert.ThrowsExactlyAsync<RemoteHistoryConflictException>(
            () => adapter.UploadHistoryAsync(new HistoryRecordDto { Hash = "hash" }, null, cancellationToken: token));
        Assert.IsNotNull(conflict.ServerRecord);
        Assert.AreEqual(supportsName ? "server" : null, conflict.ServerRecord.Name);
        Assert.AreEqual(supportsTags, conflict.ServerRecord.Tags is not null);
    }

    [TestMethod]
    public async Task CancellingOneUpload_LeavesTheSharedCapabilityProbeAvailableToAnother()
    {
        var capabilities = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var record = new HistoryRecordDto { Hash = "hash" };
        using var handler = new UploadHandler(record, capabilities.Task);
        using var adapter = CreateAdapter(handler);
        using var first = new CancellationTokenSource();
        var cancelled = adapter.UploadHistoryAsync(record, null, cancellationToken: first.Token);
        var other = adapter.UploadHistoryAsync(record, null, cancellationToken: TestContext.CancellationTokenSource.Token);
        await first.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
        capabilities.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ServerCapabilitiesDto { HistorySync = true })
        });
        Assert.AreEqual(record.Hash, (await other).Hash);
    }

    [TestMethod]
    public async Task ReconnectDuringWriteProbe_DoesNotSendWithTheOldCapabilities()
    {
        var capabilities = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var record = new HistoryRecordDto { Hash = "hash", Name = "local" };
        using var handler = new UploadHandler(record, capabilities.Task);
        using var adapter = CreateAdapter(handler);
        var upload = adapter.UploadHistoryAsync(record, null, cancellationToken: TestContext.CancellationTokenSource.Token);
        adapter.StopListening();
        capabilities.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ServerCapabilitiesDto { HistorySync = true })
        });
        await Assert.ThrowsExactlyAsync<RemoteServerException>(() => upload);
        Assert.AreEqual(0, handler.Uploads);
    }

    private static OfficialAdapter CreateAdapter(HttpMessageHandler handler)
    {
        var adapter = new OfficialAdapter(Mock.Of<ILogger>(), Mock.Of<IAppConfig>());
        var clientField = typeof(OfficialAdapter).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)clientField.GetValue(adapter)!).Dispose();
        clientField.SetValue(adapter, new HttpClient(handler) { BaseAddress = new Uri("https://history.test/") });
        return adapter;
    }

    private sealed class MetadataHandler(bool supportsTags, bool supportsName) : HttpMessageHandler
    {
        public bool Conflict { get; init; }
        public string? UploadName { get; private set; }
        public string? UploadTags { get; private set; }
        public JsonElement Patch { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath == "/api/capabilities")
            {
                var capabilities = new ServerCapabilitiesDto { HistorySync = true, HistoryTags = supportsTags, HistoryName = supportsName };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(capabilities) };
            }
            if (request.Method == HttpMethod.Patch)
            {
                Patch = await request.Content!.ReadFromJsonAsync<JsonElement>(token);
                return new(HttpStatusCode.OK);
            }
            foreach (var part in (MultipartFormDataContent)request.Content!)
            {
                var field = part.Headers.ContentDisposition?.Name?.Trim('"');
                if (field == "name")
                    UploadName = await part.ReadAsStringAsync(token);
                if (field == "Tags")
                    UploadTags = await part.ReadAsStringAsync(token);
            }
            var record = ServerRecord();
            if (Conflict)
                return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(record.ToUpdateDto()) };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(record) };
        }

        private HistoryRecordDto ServerRecord() => new()
        {
            Hash = "hash",
            Tags = JsonSerializer.Deserialize<string[]>(UploadTags ?? "[\"server\"]"),
            Name = UploadName ?? "server"
        };
    }

    private sealed class UploadHandler(HistoryRecordDto? accepted, Task<HttpResponseMessage>? capabilities = null) : HttpMessageHandler
    {
        public int Uploads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath == "/api/capabilities")
                return capabilities?.WaitAsync(token) ?? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new ServerCapabilitiesDto { HistorySync = true }) });
            Uploads++;
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("/api/history", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(accepted)
            });
        }
    }
}
