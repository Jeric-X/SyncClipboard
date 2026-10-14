using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Shared.Profiles;
using System.Reflection;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryRequestCancellationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task CancelAndWaitAsync_CancelsInFlightHistoryPageRequest()
    {
        using var handler = new BlockingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://history.test/") };
        using var adapter = new OfficialAdapter(Mock.Of<ILogger>(), Mock.Of<IAppConfig>());
        var clientField = typeof(OfficialAdapter).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)clientField.GetValue(adapter)!).Dispose();
        clientField.SetValue(adapter, client);

        // Exercise the real pagination-to-HTTP path while excluding database/application startup.
        var fetch = typeof(HistorySyncer).GetMethod("FetchRemoteRangeAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var runner = new SingletonTask(token => (Task<List<HistoryRecordDto>>)fetch.Invoke(null,
            [adapter, null, null, ProfileTypeFilter.All, null, null, 1, null, false, token])!);
        var syncing = runner.Run();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);

        await runner.CancelAndWaitAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        await syncing;
        Assert.IsTrue(handler.RequestCancelled);
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool RequestCancelled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("The request should be cancelled.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                RequestCancelled = true;
                throw;
            }
        }
    }
}
