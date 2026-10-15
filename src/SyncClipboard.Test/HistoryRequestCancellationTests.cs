using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using System.Runtime.CompilerServices;
using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Shared.Profiles;
using System.Reflection;
using ClientHistoryService = SyncClipboard.Core.UserServices.ClipboardService.HistoryService;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryRequestCancellationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("caller")]
    [DataRow("disable")]
    [DataRow("server")]
    public async Task FullSync_CancellationStopsHistoryPageRequest(string change)
    {
        using var handler = new BlockingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://history.test/") };
        using var adapter = new OfficialAdapter(Mock.Of<ILogger>(), Mock.Of<IAppConfig>());
        var clientField = typeof(OfficialAdapter).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)clientField.GetValue(adapter)!).Dispose();
        clientField.SetValue(adapter, client);

        var server = new Mock<IOfficialSyncServer>();
        server.As<IRemoteClipboardServer>();
        server.Setup(s => s.GetHistoryAsync(It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<ProfileTypeFilter>(), It.IsAny<string?>(), It.IsAny<bool?>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((int page, DateTimeOffset? before, DateTimeOffset? after, DateTimeOffset? modifiedAfter,
                ProfileTypeFilter types, string? search, bool? starred, bool sort, CancellationToken token) =>
                adapter.GetHistoryAsync(page, before, after, modifiedAfter, types, search, starred, sort, token));
        var factory = CreateFactory((IRemoteClipboardServer)server.Object);
        var runtime = new MemoryConfig();
        runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = true });
        using var session = new HistorySyncSession(factory, runtime);
        var manager = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
        var syncer = new HistorySyncer(manager, factory, Mock.Of<ILogger>(), null!, null!, runtime, session);
        server.Setup(s => s.GetServerTimeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => DateTimeOffset.Now);
        using var queue = new HistoryTransferQueue(Mock.Of<ILogger>(), factory, manager, null!, null!, runtime);
        var service = (ClientHistoryService)RuntimeHelpers.GetUninitializedObject(typeof(ClientHistoryService));
        SetServiceField(service, "runTimeConfig", runtime);
        SetServiceField(service, "syncSession", session);
        SetServiceField(service, "_historySyncServer", server.Object);
        SetServiceField(service, "historySyncer", syncer);
        SetServiceField(service, "historyManager", manager);
        SetServiceField(service, "historyTransferQueue", queue);
        SetServiceField(service, "trayIcon", Mock.Of<ITrayIcon>());
        var syncTask = typeof(ClientHistoryService).GetMethod("SyncTaskImpl", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var runner = new SingletonTask(token => (Task)syncTask.Invoke(service, [token])!);
        var syncing = runner.Run();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);

        if (change == "caller")
            await runner.CancelAndWaitAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        else if (change == "disable")
            runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = false });
        else
            NotifyServerChanged(factory);
        if (change == "caller")
            await syncing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        else
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                syncing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token));
        Assert.IsTrue(handler.RequestCancelled);
        if (change == "caller")
            Assert.IsFalse(session.Token.IsCancellationRequested);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SingleRecordSession_CancelsInFlightRequest(bool serverChanged)
    {
        var server = new Mock<IOfficialSyncServer>();
        server.As<IRemoteClipboardServer>();
        var factory = CreateFactory((IRemoteClipboardServer)server.Object);
        var runtime = new MemoryConfig();
        runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = true });
        using var session = new HistorySyncSession(factory, runtime);
        var manager = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
        var syncer = new HistorySyncer(manager, factory, Mock.Of<ILogger>(), null!, null!, runtime, session);
        var record = new HistoryRecord { Hash = "CANCEL", SyncStatus = HistorySyncStatus.NeedSync, Version = 3 };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Setup(s => s.UpdateHistoryAsync(It.IsAny<ProfileType>(), It.IsAny<string>(),
            It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProfileType _, string _, HistoryRecordUpdateDto _, CancellationToken token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            });
        var syncing = syncer.SyncOneAsync(record, session.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        if (serverChanged)
            NotifyServerChanged(factory);
        else
            runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = false });
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            syncing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token));
        Assert.AreEqual(HistorySyncStatus.NeedSync, record.SyncStatus);
        Assert.AreEqual(3, record.Version);
    }

    private static void SetServiceField(ClientHistoryService service, string name, object value)
    {
        typeof(ClientHistoryService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, value);
    }

    private static RemoteClipboardServerFactory CreateFactory(IRemoteClipboardServer server)
    {
        var factory = (RemoteClipboardServerFactory)RuntimeHelpers.GetUninitializedObject(typeof(RemoteClipboardServerFactory));
        typeof(RemoteClipboardServerFactory).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(factory, server);
        typeof(RemoteClipboardServerFactory).GetField("_serverStateLock", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(factory, new Lock());
        return factory;
    }

    private static void NotifyServerChanged(RemoteClipboardServerFactory factory)
    {
        var changed = (EventHandler?)typeof(RemoteClipboardServerFactory)
            .GetField("CurrentServerChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(factory);
        changed?.Invoke(factory, EventArgs.Empty);
    }

    private sealed class MemoryConfig : ConfigBase
    {
        protected override bool Save() => true;
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
