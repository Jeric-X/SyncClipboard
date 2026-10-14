using Moq;
using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using System.Runtime.CompilerServices;
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

    [TestMethod]
    [DataRow("disable", "success")]
    [DataRow("disable", "404")]
    [DataRow("disable", "409")]
    [DataRow("server", "success")]
    [DataRow("server", "404")]
    [DataRow("server", "409")]
    [DataRow("stop", "success")]
    [DataRow("dispose", "success")]
    public async Task SingleSync_CancelsOldSessionAndRejectsLateResponses(string change, string response)
    {
        await using var f = await SyncFixture.CreateAsync();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Server.Setup(s => s.UpdateHistoryAsync(It.IsAny<ProfileType>(), It.IsAny<string>(),
            It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProfileType _, string _, HistoryRecordUpdateDto _, CancellationToken token) =>
            {
                started.SetResult(token);
                await release.Task;
                if (response == "404")
                    throw new RemoteHistoryNotFoundException("not found");
                if (response == "409")
                    throw new RemoteHistoryConflictException("conflict", new() { Version = 99, Starred = true });
            });
        var record = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(TestContext.CancellationTokenSource.Token);
        var syncing = f.Syncer.SyncOneAsync(record, TestContext.CancellationTokenSource.Token);
        var requestToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        try
        {
            f.ChangeSession(change);
            Assert.IsTrue(requestToken.IsCancellationRequested);
        }
        finally
        {
            release.SetResult();
        }
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => syncing);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(HistorySyncStatus.NeedSync, saved.SyncStatus);
        Assert.AreEqual(3, saved.Version);
        Assert.IsFalse(saved.Stared);
        Assert.AreEqual(HistorySyncStatus.NeedSync, record.SyncStatus);

        if (change != "dispose")
        {
            f.Runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = true });
            typeof(HistorySyncSession).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(f.Session, null);
            f.Server.Setup(s => s.UpdateHistoryAsync(It.IsAny<ProfileType>(), It.IsAny<string>(),
                It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            await f.Syncer.SyncOneAsync(record, TestContext.CancellationTokenSource.Token);
            saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(TestContext.CancellationTokenSource.Token);
            Assert.AreEqual(HistorySyncStatus.Synced, saved.SyncStatus);
        }
    }

    [TestMethod]
    public async Task RecordEventSync_CancelsHttpWhenHistorySyncIsDisabled()
    {
        await using var f = await SyncFixture.CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Server.Setup(s => s.UpdateHistoryAsync(It.IsAny<ProfileType>(), It.IsAny<string>(),
            It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProfileType _, string _, HistoryRecordUpdateDto _, CancellationToken token) =>
            {
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    cancelled.SetResult();
                    throw;
                }
            });
        // Use the real Manager event -> Syncer path, whose caller supplies CancellationToken.None.
        var record = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(TestContext.CancellationTokenSource.Token);
        await f.Manager.PersistServerSyncedAsync(record, TestContext.CancellationTokenSource.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        f.ChangeSession("disable");
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(HistorySyncStatus.NeedSync, saved.SyncStatus);
    }

    [TestMethod]
    public async Task FullSync_CallerCancellationDoesNotCancelIndependentSingleSync()
    {
        await using var f = await SyncFixture.CreateAsync();
        var pageStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var singleStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Server.Setup(s => s.GetHistoryAsync(It.IsAny<int>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<ProfileTypeFilter>(),
            It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(async (int _, DateTimeOffset? _, DateTimeOffset? _, DateTimeOffset? _, ProfileTypeFilter _,
                string? _, bool? _, bool _, CancellationToken token) =>
            {
                pageStarted.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return [];
            });
        f.Server.Setup(s => s.UpdateHistoryAsync(It.IsAny<ProfileType>(), It.IsAny<string>(),
            It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProfileType _, string _, HistoryRecordUpdateDto _, CancellationToken token) =>
            {
                singleStarted.SetResult(token);
                await Task.Delay(Timeout.Infinite, token);
            });
        using var caller = new CancellationTokenSource();
        var all = f.Syncer.SyncAllAsync(null, caller.Token);
        var record = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(TestContext.CancellationTokenSource.Token);
        var single = f.Syncer.SyncOneAsync(record, TestContext.CancellationTokenSource.Token);
        await pageStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        var singleToken = await singleStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        caller.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => all);
        Assert.IsFalse(singleToken.IsCancellationRequested);
        f.ChangeSession("disable");
        await Assert.ThrowsAsync<OperationCanceledException>(() => single);
    }

    [TestMethod]
    [DataRow(false, "disable")]
    [DataRow(false, "server")]
    [DataRow(true, "disable")]
    [DataRow(true, "server")]
    [DataRow(false, "caller")]
    [DataRow(true, "caller")]
    public async Task Transfer_SessionCancellationRespectsManualPromotion(bool promote, string change)
    {
        await using var f = await SyncFixture.CreateAsync();
        await using var queue = new HistoryTransferQueue(Mock.Of<ILogger>(), f.Session, f.Manager,
            Mock.Of<IProfileEnv>(), null!);
        var profile = new TextProfile(new SyncClipboard.Shared.Profiles.Models.ProfilePersistentInfo
        {
            Type = ProfileType.Text,
            Text = "cancel",
            Hash = "CANCEL",
            Size = 6
        });
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Server.Setup(s => s.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken token) =>
            {
                started.SetResult(token);
                await release.Task;
                return new HistoryRecordDto { Type = ProfileType.Text, Hash = "REMOTE" };
            });
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
        var transfer = await queue.EnqueueUpload(profile, ct: caller.Token);
        var requestToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        Task? manual = promote ? queue.Upload(profile, ct: TestContext.CancellationTokenSource.Token) : null;
        try
        {
            if (change == "caller")
                caller.Cancel();
            else
                f.ChangeSession(change);
            Assert.AreEqual(!promote || change == "server", requestToken.IsCancellationRequested);
        }
        finally
        {
            release.SetResult();
        }
        if (manual is not null)
        {
            if (change == "server")
                await Assert.ThrowsAsync<OperationCanceledException>(() => manual);
            else
                await manual.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        }
        await queue.DisposeAsync();
        Assert.AreEqual(promote && change != "server" ? TransferTaskStatus.Completed : TransferTaskStatus.Cancelled,
            await transfer.CompletionSource.Task);
        if (!promote || change == "server")
            Assert.AreEqual(1, await f.Db.HistoryRecords.CountAsync(TestContext.CancellationTokenSource.Token));
    }

    [TestMethod]
    [DataRow("disable")]
    [DataRow("server")]
    public async Task Transfer_WaitingRegistrationCannotEnterReplacementSession(string change)
    {
        await using var f = await SyncFixture.CreateAsync();
        await using var queue = new HistoryTransferQueue(Mock.Of<ILogger>(), f.Session, f.Manager,
            Mock.Of<IProfileEnv>(), null!);
        await queue.ActiveTaskAddMutex.WaitAsync(TestContext.CancellationTokenSource.Token);
        var pending = queue.EnqueueUpload(new TextProfile("late registration"), ct: TestContext.CancellationTokenSource.Token);
        try
        {
            f.ChangeSession(change);
            f.Runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = true });
        }
        finally
        {
            queue.ActiveTaskAddMutex.Release();
        }
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.HasCount(0, await queue.GetAllActiveTasks(TestContext.CancellationTokenSource.Token));
        f.Server.Verify(s => s.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class MemoryConfig : ConfigBase
    {
        protected override bool Save() => true;
    }

    private sealed class MemoryDb : HistoryDbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=:memory:");
    }

    private sealed class SyncFixture : IAsyncDisposable
    {
        public MemoryDb Db { get; } = new();
        public MemoryConfig Runtime { get; } = new();
        public Mock<IOfficialSyncServer> Server { get; private set; } = NewServer();
        public HistoryManager Manager { get; } = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
        public HistorySyncer Syncer { get; private set; } = null!;
        public HistorySyncSession Session { get; private set; } = null!;
        private readonly RemoteClipboardServerFactory factory =
            (RemoteClipboardServerFactory)RuntimeHelpers.GetUninitializedObject(typeof(RemoteClipboardServerFactory));
        private readonly SemaphoreSlim dbLock = new(1, 1);

        private static Mock<IOfficialSyncServer> NewServer()
        {
            var server = new Mock<IOfficialSyncServer>();
            server.As<IRemoteClipboardServer>();
            return server;
        }

        private static void Set(object target, string field, object value) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

        public static async Task<SyncFixture> CreateAsync()
        {
            var f = new SyncFixture();
            await f.Db.Database.OpenConnectionAsync();
            await f.Db.Database.EnsureCreatedAsync();
            f.Db.HistoryRecords.Add(new HistoryRecord
            {
                Hash = "CANCEL",
                Text = "cancel",
                Type = ProfileType.Text,
                Version = 3,
                SyncStatus = HistorySyncStatus.NeedSync
            });
            await f.Db.SaveChangesAsync();
            Set(f.Manager, "_dbContext", f.Db);
            Set(f.Manager, "_dbSemaphore", f.dbLock);
            Set(f.factory, "_serverStateLock", typeof(Lock).GetConstructor(Type.EmptyTypes)!.Invoke(null));
            Set(f.factory, "_current", f.Server.Object);
            f.Runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = true });
            f.Session = new HistorySyncSession(f.factory, f.Runtime);
            f.Syncer = new HistorySyncer(f.Manager, Mock.Of<ILogger>(), null!, null!, f.Runtime, f.Session);
            return f;
        }

        public void ChangeSession(string change)
        {
            switch (change)
            {
                case "disable":
                    Runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = false });
                    break;
                case "server":
                    Server = NewServer();
                    Set(factory, "_current", Server.Object);
                    var handler = (EventHandler?)typeof(RemoteClipboardServerFactory)
                        .GetField("CurrentServerChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(factory);
                    handler?.Invoke(factory, EventArgs.Empty);
                    break;
                case "stop":
                    typeof(HistorySyncSession).GetMethod("Stop", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(Session, null);
                    break;
                case "dispose":
                    Session.Dispose();
                    break;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Syncer.Dispose();
            Session.Dispose();
            await Db.DisposeAsync();
            dbLock.Dispose();
        }
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
