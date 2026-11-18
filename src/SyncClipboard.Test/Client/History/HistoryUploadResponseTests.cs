using Moq;
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
    [DataRow(null)]
    [DataRow("[]")]
    [DataRow("[\"Work\",\"工作\"]")]
    public async Task UploadHistoryAsync_PreservesTagIntentThroughServerAcknowledgement(string? tagsJson)
    {
        var local = new HistoryRecord { Hash = "hash", Tags = ["local"] };
        var incoming = local.ToHistoryRecordDto();
        incoming.Tags = tagsJson is null ? null : JsonSerializer.Deserialize<string[]>(tagsJson);
        using var handler = new TagAcknowledgementHandler();
        using var adapter = CreateAdapter(handler);

        var accepted = await adapter.UploadHistoryAsync(incoming, null,
            cancellationToken: TestContext.CancellationTokenSource.Token);
        local.ApplyChangesFromRemote(accepted);

        string[] expected = tagsJson is null ? ["server"] : JsonSerializer.Deserialize<string[]>(tagsJson)!;
        CollectionAssert.AreEqual(expected, local.Tags);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("应用图标")]
    [DataRow("null")]
    public async Task UploadHistoryAsync_PreservesTitleIntent(string? title)
    {
        using var handler = new TitleAcknowledgementHandler();
        using var adapter = CreateAdapter(handler);
        var incoming = new HistoryRecordDto { Hash = "hash", Title = title };

        var accepted = await adapter.UploadHistoryAsync(incoming, null,
            cancellationToken: TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(title is not null, handler.TitleProvided);
        Assert.AreEqual(title ?? "server", accepted.Title);
    }

    private sealed class TitleAcknowledgementHandler : HttpMessageHandler
    {
        public bool TitleProvided { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var form = (MultipartFormDataContent)request.Content!;
            var field = form.SingleOrDefault(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "title");
            TitleProvided = field is not null;
            var title = field is null ? "server" : await field.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new HistoryRecordDto { Hash = "hash", Title = title })
            };
        }
    }

    private static OfficialAdapter CreateAdapter(HttpMessageHandler handler)
    {
        var adapter = new OfficialAdapter(Mock.Of<ILogger>(), Mock.Of<IAppConfig>());
        var clientField = typeof(OfficialAdapter).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)clientField.GetValue(adapter)!).Dispose();
        clientField.SetValue(adapter, new HttpClient(handler) { BaseAddress = new Uri("https://history.test/") });
        return adapter;
    }

    private sealed class TagAcknowledgementHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var form = (MultipartFormDataContent)request.Content!;
            var tagField = form.SingleOrDefault(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "tags");
            // Mirror the server's omitted/null-preserves, explicit-array-replaces contract.
            string[] savedTags = ["server"];
            if (tagField is not null)
                savedTags = JsonSerializer.Deserialize<string[]>(await tagField.ReadAsStringAsync(token)) ?? savedTags;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new HistoryRecordDto { Hash = "hash", Tags = savedTags })
            };
        }
    }

    private sealed class UploadHandler(HistoryRecordDto? accepted) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("/api/history", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(accepted)
            });
        }
    }
}
