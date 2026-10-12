using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using SyncClipboard.Server.Core.Models;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;

namespace SyncClipboard.Test;

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
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://history.test/") };
        using var adapter = new OfficialAdapter(Mock.Of<ILogger>(), Mock.Of<IAppConfig>());
        var clientField = typeof(OfficialAdapter).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)clientField.GetValue(adapter)!).Dispose();
        clientField.SetValue(adapter, client);
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
