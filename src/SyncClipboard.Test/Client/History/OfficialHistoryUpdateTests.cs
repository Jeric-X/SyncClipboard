using System.Net;
using System.Reflection;
using System.Text.Json;
using Moq;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class OfficialHistoryUpdateTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Update_ReturnsFinalMetadataForSuccessAndConflict(bool conflict)
    {
        var saved = new HistoryRecordUpdateDto
        {
            Tags = ["local"],
            Version = 10,
            Starred = true,
            LastModified = DateTimeOffset.UtcNow
        };
        using var adapter = CreateAdapter(conflict ? HttpStatusCode.Conflict : HttpStatusCode.OK,
            JsonSerializer.Serialize(saved, JsonSerializerOptions.Web));
        HistoryRecordUpdateDto? result;
        if (conflict)
        {
            var error = await Assert.ThrowsExactlyAsync<RemoteHistoryConflictException>(() =>
                adapter.UpdateHistoryAsync(ProfileType.Text, "HASH", new(), Token));
            result = error.ServerRecord;
        }
        else
        {
            result = await adapter.UpdateHistoryAsync(ProfileType.Text, "HASH", new(), Token);
        }
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(saved.Tags, result.Tags);
        Assert.AreEqual(saved.Version, result.Version);
        Assert.AreEqual(saved.Starred, result.Starred);
        Assert.AreEqual(saved.LastModified, result.LastModified);
    }

    [TestMethod]
    public async Task Update_LegacyEmptySuccessRemainsSupported()
    {
        using var adapter = CreateAdapter(HttpStatusCode.OK, "");
        Assert.IsNull(await adapter.UpdateHistoryAsync(ProfileType.Text, "HASH", new(), Token));
    }

    private static OfficialAdapter CreateAdapter(HttpStatusCode status, string body)
    {
        var adapter = new OfficialAdapter(Mock.Of<ILogger>(), Mock.Of<IAppConfig>());
        var field = typeof(OfficialAdapter).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)field.GetValue(adapter)!).Dispose();
        field.SetValue(adapter, new HttpClient(new UpdateHandler(status, body)) { BaseAddress = new Uri("https://history.test/") });
        return adapter;
    }

    private sealed class UpdateHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.AreEqual(HttpMethod.Patch, request.Method);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
