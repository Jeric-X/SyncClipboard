using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObservableCollections;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using System.Threading.Channels;
using SyncClipboard.Core;
using Moq;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test;

[TestClass]
[DoNotParallelize]
public class HistoryUploadCoordinationTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void SetObjectField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ImmediateUploadAndQueuedUpload_ShareOneTransfer(bool queueFirst)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("shared transfer");
        await f.Manager.AddLocalProfile(profile, token: Token);
        var entered = Signal();
        var release = Signal();
        f.Server.Setup(x => x.UploadHistoryAsync(It.IsAny<HistoryRecordDto>(), It.IsAny<FileHashInfo?>(),
            It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns<HistoryRecordDto, FileHashInfo?, IProgress<HttpDownloadProgress>?, CancellationToken>(async (dto, _, _, ct) =>
            {
                Interlocked.Increment(ref f.Uploads);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct);
                f.RemoteRecord = dto;
                return dto;
            });
        Task immediate;
        TransferTask queued;
        if (queueFirst)
        {
            // A stopped background queue must still allow clipboard callers to promote the pending task.
            SetObjectField(f.Queue, "_isQueueStopped", true);
            queued = await f.Queue.EnqueueUpload(profile, ct: Token);
            immediate = f.Queue.Upload(profile, ct: Token);
        }
        else
        {
            immediate = f.Queue.Upload(profile, ct: Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            queued = await f.Queue.EnqueueUpload(profile, ct: Token);
        }
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        var duplicate = f.Queue.Upload(new TextProfile("shared transfer"), ct: Token);
        release.SetResult(true);
        await Task.WhenAll(immediate, duplicate).WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.AreEqual(TransferTaskStatus.Completed, await queued.CompletionSource.Task.WaitAsync(Token));
        Assert.AreEqual(1, f.Uploads);
        Assert.AreEqual(0, f.Updates);
    }

    [TestMethod]
    public async Task ConcurrentRegistrations_ExecuteOneUpload()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("concurrent registration");
        _ = f.CreateSyncer();
        await profile.GetHash(Token);
        await f.Queue.ActiveTaskAddMutex.WaitAsync(Token);
        var queued = f.Queue.EnqueueUpload(profile, ct: Token);
        var first = f.Queue.Upload(profile, ct: Token);
        var second = f.Queue.Upload(profile, ct: Token);
        f.Queue.ActiveTaskAddMutex.Release();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.AreEqual(TransferTaskStatus.Completed, await (await queued).CompletionSource.Task.WaitAsync(Token));
        Assert.AreEqual(1, f.Uploads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExistingRemote_SynchronizesOnlyNewerProperties(bool remoteNewer)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var source = Path.Combine(f.GetPersistentDir(), "existing.txt");
        await File.WriteAllTextAsync(source, "existing metadata", Token);
        var profile = new FileProfile(source);
        await f.Manager.AddLocalProfile(profile, token: Token);
        var record = await f.Db.HistoryRecords.SingleAsync(Token);
        record.Version = 10;
        record.Stared = true;
        record.LastAccessed = DateTime.UtcNow;
        await f.Db.SaveChangesAsync(Token);
        f.RemoteRecord = record.ToHistoryRecordDto();
        f.RemoteRecord.Version = remoteNewer ? 11 : 9;
        f.RemoteRecord.Starred = false;
        f.RemoteRecord.LastAccessed = record.LastAccessed.AddMinutes(-1);
        record.FilePath = [];
        record.TransferDataFile = null;
        record.TransferDataHash = null;
        record.IsLocalFileReady = false;
        await f.Db.SaveChangesAsync(Token);
        var patchEntered = Signal();
        var releasePatch = Signal();
        f.Server.Setup(x => x.UpdateHistoryAsync(It.IsAny<ProfileType>(), It.IsAny<string>(),
            It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
            .Returns<ProfileType, string, HistoryRecordUpdateDto, CancellationToken>(async (_, _, dto, ct) =>
            {
                Interlocked.Increment(ref f.Updates);
                patchEntered.TrySetResult(true);
                await releasePatch.Task.WaitAsync(ct);
                f.RemoteRecord.Version = dto.Version!.Value;
                f.RemoteRecord.Starred = dto.Starred!.Value;
            });
        _ = f.CreateSyncer();
        var synced = Signal();
        f.Manager.HistoryUpdated += updated =>
        {
            if (updated.SyncStatus == HistorySyncStatus.Synced)
                synced.TrySetResult(true);
        };
        try
        {
            await f.Queue.Upload(profile, ct: Token);
            if (!remoteNewer)
                await patchEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.AreEqual(remoteNewer ? 0 : 1, f.Updates);
        }
        finally
        {
            releasePatch.TrySetResult(true);
        }
        await synced.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        await f.Queue.Upload(profile, ct: Token);
        Assert.AreEqual(0, f.Uploads);
        Assert.AreEqual(remoteNewer ? 0 : 1, f.Updates);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(!remoteNewer, saved.Stared);
        Assert.AreEqual(remoteNewer ? 11 : 10, saved.Version);
        Assert.AreEqual(HistorySyncStatus.Synced, saved.SyncStatus);
        Assert.IsTrue(saved.IsLocalFileReady);
        Assert.IsTrue(await saved.ToProfile().IsDataComplete(false, Token));
    }

    [TestMethod]
    public async Task EditDuringUpload_UsesServerPropertiesWhenVersionsTie()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("edit in flight");
        await f.Manager.AddLocalProfile(profile, token: Token);
        var expectedVersion = (await f.Manager.GetOrCreateHistoryRecord(profile, Token)).Version + 1;
        var entered = Signal();
        var release = Signal();
        f.Server.Setup(x => x.UploadHistoryAsync(It.IsAny<HistoryRecordDto>(), It.IsAny<FileHashInfo?>(),
            It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns<HistoryRecordDto, FileHashInfo?, IProgress<HttpDownloadProgress>?, CancellationToken>(async (dto, _, _, ct) =>
            {
                f.Uploads++;
                entered.SetResult(true);
                await release.Task.WaitAsync(ct);
                dto.Version = expectedVersion;
                f.RemoteRecord = dto;
                return dto;
            });
        var upload = f.Queue.Upload(profile, ct: Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        var edit = await f.Manager.GetOrCreateHistoryRecord(profile, Token);
        edit.Stared = true;
        await f.Manager.UpdateHistoryProperty(edit, Token);
        _ = f.CreateSyncer();
        var synced = Signal();
        f.Manager.HistoryUpdated += updated =>
        {
            if (updated.SyncStatus == HistorySyncStatus.Synced)
                synced.TrySetResult(true);
        };
        release.SetResult(true);
        await upload.WaitAsync(TimeSpan.FromSeconds(5), Token);
        await synced.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.IsFalse(saved.Stared);
        Assert.IsFalse(f.RemoteRecord!.Starred);
        Assert.AreEqual(expectedVersion, saved.Version);
        Assert.AreEqual(HistorySyncStatus.Synced, saved.SyncStatus);
        Assert.AreEqual(1, f.Uploads);
        Assert.AreEqual(0, f.Updates);
    }

    [TestMethod]
    public async Task PropertyUpdate404_FallsBackToContentUpload()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("missing after get");
        await f.Manager.AddLocalProfile(profile, token: Token);
        var record = await f.Db.HistoryRecords.SingleAsync(Token);
        record.Version = 2;
        await f.Db.SaveChangesAsync(Token);
        f.RemoteRecord = record.ToHistoryRecordDto();
        f.RemoteRecord.Version = 1;
        f.Server.Setup(x => x.UpdateHistoryAsync(It.IsAny<ProfileType>(), It.IsAny<string>(),
            It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                f.RemoteRecord = null;
                throw new RemoteHistoryNotFoundException("missing");
            });
        await f.CreateSyncer().SyncOneAsync(record, Token);
        await f.Queue.WaitAllTasks(Token).WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.AreEqual(1, f.Uploads);
        Assert.AreEqual(2, f.RemoteRecord!.Version);
    }

    [TestMethod]
    public async Task PropertyUpdate409_AcceptsRemoteWithoutUploadingContent()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("conflict after get");
        await f.Manager.AddLocalProfile(profile, token: Token);
        var record = await f.Db.HistoryRecords.SingleAsync(Token);
        record.Version = 2;
        await f.Db.SaveChangesAsync(Token);
        f.RemoteRecord = record.ToHistoryRecordDto();
        f.RemoteRecord.Version = 1;
        f.Server.Setup(x => x.UpdateHistoryAsync(It.IsAny<ProfileType>(), It.IsAny<string>(),
            It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RemoteHistoryConflictException("conflict", new HistoryRecordUpdateDto
            {
                Version = 3,
                Starred = true,
                IsDelete = false,
                LastModified = record.LastModified
            }));
        await f.CreateSyncer().SyncOneAsync(record, Token);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(3, saved.Version);
        Assert.IsTrue(saved.Stared);
        Assert.AreEqual(0, f.Uploads);
    }

    [TestMethod]
    public async Task AddedRecord_WithClipboardSyncAndPullEnabled_StillUploads()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var previous = AppCore.TryGetCurrent();
        var constructor = typeof(ConfigManager).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == 2 && c.GetParameters()[0].ParameterType != typeof(string));
        var config = (ConfigManager)constructor.Invoke([null, null]);
        typeof(ConfigBase).GetProperty(nameof(ConfigBase.Path))!.SetValue(config, Path.Combine(f.GetPersistentDir(), "config.json"));
        config.SetConfig(new SyncConfig { SyncSwitchOn = true, PullSwitchOn = true, EnableUploadText = true });
        using var services = new ServiceCollection().AddSingleton<IProfileEnv>(f).AddSingleton(config).BuildServiceProvider();
        var app = (AppCore)RuntimeHelpers.GetUninitializedObject(typeof(AppCore));
        SetObjectField(app, "<Services>k__BackingField", services);
        SetObjectField(app, "<ConfigManager>k__BackingField", config);
        typeof(AppCore).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, app);
        try
        {
            var runtime = new ConfigBase(Path.Combine(f.GetPersistentDir(), "runtime.json"));
            runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = true });
            using var session = new HistorySyncSession(f.Factory, runtime);
            _ = new HistorySyncer(f.Manager, f.Factory, Mock.Of<ILogger>(), f.Queue, config, runtime, session);
            var completed = Signal();
            f.Queue.TaskStatusChanged += (_, task) =>
            {
                if (task.Status == TransferTaskStatus.Completed)
                    completed.TrySetResult(true);
            };
            await f.Manager.AddLocalProfile(new TextProfile("new history while pulling"), token: Token);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.AreEqual(1, f.Uploads);
            Assert.AreEqual(HistorySyncStatus.Synced, (await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token)).SyncStatus);
        }
        finally
        {
            typeof(AppCore).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, previous);
        }
    }

    [TestMethod]
    public async Task ClearedTask_LateResponseCannotOverwriteOrRemoveReplacement()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("replacement transfer");
        await f.Manager.AddLocalProfile(profile, token: Token);
        var entered = Signal();
        var release = Signal();
        f.Server.Setup(x => x.UploadHistoryAsync(It.IsAny<HistoryRecordDto>(), It.IsAny<FileHashInfo?>(),
            It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns<HistoryRecordDto, FileHashInfo?, IProgress<HttpDownloadProgress>?, CancellationToken>(async (dto, _, _, _) =>
            {
                entered.SetResult(true);
                await release.Task;
                dto.Version = 100;
                return dto;
            });
        var registration = typeof(HistoryTransferQueue).GetMethod("GetOrAddTaskAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldTask = await (Task<TransferTask>)registration.Invoke(f.Queue,
            [TransferType.Upload, profile, true, null, Token])!;
        var execute = typeof(HistoryTransferQueue).GetMethod("ExecuteTaskAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldExecution = (Task<TransferTaskStatus>)execute.Invoke(f.Queue, [oldTask])!;
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        f.Queue.ClearQueue();
        Assert.AreEqual(TransferTaskStatus.Cancelled, await oldTask.CompletionSource.Task.WaitAsync(Token));
        var replacementEntered = Signal();
        var replacementRelease = Signal();
        f.Server.Setup(x => x.UploadHistoryAsync(It.IsAny<HistoryRecordDto>(), It.IsAny<FileHashInfo?>(),
            It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns<HistoryRecordDto, FileHashInfo?, IProgress<HttpDownloadProgress>?, CancellationToken>(async (dto, _, _, ct) =>
            {
                replacementEntered.SetResult(true);
                await replacementRelease.Task.WaitAsync(ct);
                dto.Version = 3;
                f.RemoteRecord = dto;
                return dto;
            });
        var replacement = f.Queue.Upload(profile, ct: Token);
        await replacementEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        release.SetResult(true);
        Assert.AreEqual(TransferTaskStatus.Cancelled, await oldExecution.WaitAsync(TimeSpan.FromSeconds(5), Token));
        var task = f.Queue.GetTaskByProfileId(await profile.GetProfileId(Token));
        Assert.IsNotNull(task);
        Assert.AreNotSame(oldTask, task);
        Assert.IsFalse(replacement.IsCompleted);
        replacementRelease.SetResult(true);
        await replacement.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.AreEqual(3, (await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token)).Version);
    }

    [TestMethod]
    public async Task PromotedUpload_ContinuesWhenPendingQueueFails()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("promoted pending transfer");
        await f.Manager.AddLocalProfile(profile, token: Token);
        SetObjectField(f.Queue, "_isQueueStopped", true);
        var queued = await f.Queue.EnqueueUpload(profile, ct: Token);
        var entered = Signal();
        var release = Signal();
        f.Server.Setup(x => x.UploadHistoryAsync(It.IsAny<HistoryRecordDto>(), It.IsAny<FileHashInfo?>(),
            It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns<HistoryRecordDto, FileHashInfo?, IProgress<HttpDownloadProgress>?, CancellationToken>(async (dto, _, _, ct) =>
            {
                entered.SetResult(true);
                await release.Task.WaitAsync(ct);
                f.RemoteRecord = dto;
                return dto;
            });
        var immediate = f.Queue.Upload(profile, ct: Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        InvokeQueue(f.Queue, "MarkAllQueuedTasksAsFailed");
        var completedBeforeResponse = queued.CompletionSource.Task.IsCompleted;
        release.SetResult(true);
        await immediate.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.IsFalse(completedBeforeResponse);
        Assert.AreEqual(TransferTaskStatus.Completed, queued.Status);
    }

    [TestMethod]
    public async Task CompletedUpload_DisposesItsCancellationSource()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("dispose completed transfer");
        await f.Manager.AddLocalProfile(profile, token: Token);
        var task = await f.Queue.EnqueueUpload(profile, ct: Token);
        await task.CompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        await WaitForDisposedAsync(task.CancellationSource);
    }

    [TestMethod]
    public async Task ClearQueue_CancelsRunningRequestAndReleasesResources()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("dispose cancelled transfer");
        await f.Manager.AddLocalProfile(profile, token: Token);
        SetObjectField(f.Queue, "_isQueueStopped", true);
        var entered = Signal();
        var release = Signal();
        var notifications = Channel.CreateUnbounded<TransferTask>();
        f.Queue.TaskStatusChanged += (_, changed) => notifications.Writer.TryWrite(changed);
        CancellationToken requestToken = default;
        TransferTask? replacement = null;
        f.Server.Setup(x => x.UploadHistoryAsync(It.IsAny<HistoryRecordDto>(), It.IsAny<FileHashInfo?>(),
            It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns<HistoryRecordDto, FileHashInfo?, IProgress<HttpDownloadProgress>?, CancellationToken>(async (_, _, _, ct) =>
            {
                requestToken = ct;
                using var registration = ct.Register(() =>
                {
                    // Cancellation callbacks may synchronously wait for a new registration on another thread.
                    replacement = Task.Run(() => f.Queue.EnqueueUpload(profile, ct: Token), Token)
                        .WaitAsync(TimeSpan.FromSeconds(5), Token).GetAwaiter().GetResult();
                });
                entered.SetResult(true);
                await release.Task;
                throw new IOException("request failed after cancellation");
            });
        var register = typeof(HistoryTransferQueue).GetMethod("GetOrAddTaskAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = await (Task<TransferTask>)register.Invoke(f.Queue,
            [TransferType.Upload, profile, true, null, Token])!;
        var pending = await f.Queue.EnqueueUpload(new TextProfile("another cancelled transfer"), ct: Token);
        var pendingToken = pending.CancellationSource.Token;
        var cancellationCallbacks = 0;
        void CancelCallback()
        {
            if (Interlocked.Increment(ref cancellationCallbacks) == 1)
                throw new InvalidOperationException("first cancellation callback failed");
        }
        using var runningRegistration = task.CancellationSource.Token.Register(CancelCallback);
        using var pendingRegistration = pendingToken.Register(CancelCallback);
        var execute = typeof(HistoryTransferQueue).GetMethod("ExecuteTaskAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var execution = (Task<TransferTaskStatus>)execute.Invoke(f.Queue, [task])!;
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        try
        {
            f.Queue.ClearQueue();
            Assert.IsTrue(requestToken.IsCancellationRequested);
            Assert.AreEqual(TransferTaskStatus.Cancelled, await task.CompletionSource.Task.WaitAsync(Token));
            Assert.IsTrue(pendingToken.IsCancellationRequested);
            Assert.AreEqual(TransferTaskStatus.Cancelled,
                await pending.CompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5), Token));
            Assert.AreEqual(2, cancellationCallbacks);
            await WaitForDisposedAsync(task.CancellationSource);
            await WaitForDisposedAsync(pending.CancellationSource);
        }
        finally
        {
            release.TrySetResult(true);
        }
        Assert.AreEqual(TransferTaskStatus.Cancelled, await execution.WaitAsync(TimeSpan.FromSeconds(5), Token));
        Assert.IsNotNull(replacement);
        Assert.AreSame(replacement, f.Queue.GetTaskByProfileId(await profile.GetProfileId(Token)));
        Assert.IsFalse(replacement.CompletionSource.Task.IsCompleted);
        notifications.Writer.Complete();
        await AssertReplacementRemainsVisibleAsync(f, profile, notifications);
    }

    private async Task AssertReplacementRemainsVisibleAsync(
        Fixture fixture, Profile profile, Channel<TransferTask> notifications)
    {
        var dispatcher = new Mock<IThreadDispatcher>();
        dispatcher.Setup(x => x.RunOnMainThreadAsync(It.IsAny<Func<Task>>()))
            .Returns<Func<Task>>(action => action());
        var viewModel = (HistoryViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HistoryViewModel));
        var record = await fixture.Manager.GetHistoryRecord(await profile.GetHash(Token), profile.Type, Token);
        Assert.IsNotNull(record);
        var item = new HistoryRecordVM(record);
        SetObjectField(viewModel, "_threadDispatcher", dispatcher.Object);
        SetObjectField(viewModel, "_transferQueue", fixture.Queue);
        SetObjectField(viewModel, "_updateTaskQueue", notifications);
        SetObjectField(viewModel, "allHistoryItems", new ObservableList<HistoryRecordVM> { item });
        var consume = typeof(HistoryViewModel).GetMethod("ConsumeUpdateTaskQueueAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Task)consume.Invoke(viewModel, [Token])!).WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.IsTrue(item.IsUploading);
        Assert.IsTrue(item.IsUploadPending);
    }

    [TestMethod]
    public async Task MergedUpload_UsesNewRequestWhenOriginalFileIsGone()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var oldDir = Directory.CreateDirectory(Path.Combine(f.GetPersistentDir(), "old"));
        var newDir = Directory.CreateDirectory(Path.Combine(f.GetPersistentDir(), "new"));
        var oldPath = Path.Combine(oldDir.FullName, "same.txt");
        var newPath = Path.Combine(newDir.FullName, "same.txt");
        await File.WriteAllTextAsync(oldPath, "same contents", Token);
        await File.WriteAllTextAsync(newPath, "same contents", Token);
        var oldProfile = new FileProfile(oldPath);
        var newProfile = new FileProfile(newPath);
        Assert.AreEqual(await oldProfile.GetProfileId(Token), await newProfile.GetProfileId(Token));
        SetObjectField(f.Queue, "_isQueueStopped", true);
        var task = await f.Queue.EnqueueUpload(oldProfile, ct: Token);
        File.Delete(oldPath);
        var registration = typeof(HistoryTransferQueue).GetMethod("GetOrAddTaskAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task<TransferTask>)registration.Invoke(f.Queue,
            [TransferType.Upload, newProfile, true, null, Token])!;
        await f.Queue.EnqueueUpload(oldProfile, ct: Token);
        var execute = typeof(HistoryTransferQueue).GetMethod("ExecuteTaskAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Task)execute.Invoke(f.Queue, [task])!).WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.AreEqual(TransferTaskStatus.Completed, await task.CompletionSource.Task.WaitAsync(Token));
        Assert.AreEqual(1, f.Uploads);
        Assert.IsTrue((await f.Db.HistoryRecords.SingleAsync(Token)).IsLocalFileReady);
    }

    [TestMethod]
    public async Task MissingRemoteAttachment_WithoutLocalDataBecomesLocalOnly()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new FileProfile(null, "remote.txt", new string('A', 64));
        var now = DateTime.UtcNow;
        f.Db.HistoryRecords.Add(new HistoryRecord
        {
            Type = ProfileType.File,
            Hash = await profile.GetHash(Token),
            Text = "remote.txt",
            IsLocalFileReady = false,
            SyncStatus = HistorySyncStatus.NeedSync,
            Stared = true,
            Version = 5,
            LastModified = now,
            LastAccessed = now
        });
        await f.Db.SaveChangesAsync(Token);
        await f.CreateSyncer().SyncOneAsync(await f.Db.HistoryRecords.SingleAsync(Token), Token);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(HistorySyncStatus.LocalOnly, saved.SyncStatus);
        Assert.IsFalse(saved.IsLocalFileReady);
        Assert.IsFalse(saved.IsDeleted);
        Assert.IsTrue(saved.Stared);
        Assert.AreEqual(5, saved.Version);
        Assert.AreEqual(now, saved.LastModified);
        Assert.AreEqual(0, f.Uploads);
    }

    [TestMethod]
    public async Task ImmediateRequestDuringHistoryShutdown_DoesNotWaitOnCancelledTask()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("promote while disabling history");
        await f.Manager.AddLocalProfile(profile, token: Token);
        SetObjectField(f.Queue, "_isQueueStopped", true);
        var queued = await f.Queue.EnqueueUpload(profile, ct: Token);
        Task? immediate = null;
        f.Queue.TaskStatusChanged += (_, task) =>
        {
            if (ReferenceEquals(task, queued) && task.Status == TransferTaskStatus.Cancelled && immediate is null)
                immediate = f.Queue.Upload(profile, ct: Token);
        };
        InvokeQueue(f.Queue, "CancelNonImmediateTasks");
        Assert.IsNotNull(immediate);
        await immediate.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.AreEqual(TransferTaskStatus.Cancelled, await queued.CompletionSource.Task.WaitAsync(Token));
        Assert.AreEqual(1, f.Uploads);
    }

    [TestMethod]
    public async Task PropertyConflictDeletion_NotifiesRemoval()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("remote deletion conflict");
        await f.Manager.AddLocalProfile(profile, token: Token);
        var record = await f.Db.HistoryRecords.SingleAsync(Token);
        record.Version = 2;
        record.SyncStatus = HistorySyncStatus.NeedSync;
        await f.Db.SaveChangesAsync(Token);
        f.RemoteRecord = record.ToHistoryRecordDto();
        f.RemoteRecord.Version = 1;
        var syncer = f.CreateSyncer();
        f.Server.Setup(x => x.UpdateHistoryAsync(It.IsAny<ProfileType>(), It.IsAny<string>(),
            It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RemoteHistoryConflictException("deleted", new HistoryRecordUpdateDto
            {
                Version = 3,
                IsDelete = true,
                LastModified = record.LastModified
            }));
        var removed = Signal();
        f.Manager.HistoryRemoved += _ => removed.TrySetResult(true);
        await syncer.SyncOneAsync(record, Token);
        await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.IsTrue(saved.IsDeleted);
        Assert.AreEqual(HistorySyncStatus.Synced, saved.SyncStatus);
    }

    [TestMethod]
    public async Task QueueFailure_CompletesWaitingImmediateRetry()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("retry stopped by queue failure");
        await f.Manager.AddLocalProfile(profile, token: Token);
        SetObjectField(f.Queue, "_isQueueStopped", true);
        var waiting = Signal();
        f.Server.Setup(x => x.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("network failure"));
        f.Queue.TaskStatusChanged += (_, task) =>
        {
            if (task.Status == TransferTaskStatus.WaitForRetry)
                waiting.TrySetResult(true);
        };
        var immediate = f.Queue.Upload(profile, ct: Token);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        // Wait until retry registration finishes; the stopped queue cannot consume it.
        var pending = (Queue<TransferTask>)typeof(HistoryTransferQueue)
            .GetField("_pendingTasks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Queue)!;
        var queueLock = (Lock)typeof(HistoryTransferQueue)
            .GetField("_queueLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Queue)!;
        for (var i = 0; i < 100; i++)
        {
            lock (queueLock)
            {
                if (pending.Count != 0)
                    break;
            }
            await Task.Delay(10, Token);
        }
        InvokeQueue(f.Queue, "MarkAllQueuedTasksAsFailed");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => immediate.WaitAsync(TimeSpan.FromSeconds(5), Token));
    }

    [TestMethod]
    public async Task QueueFailure_CompletesRetryRegisteredAfterFailureSweep()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var first = new TextProfile("failure before queue stops");
        var second = new TextProfile("failure that stops queue");
        await f.Manager.AddLocalProfile(first, token: Token);
        await f.Manager.AddLocalProfile(second, token: Token);
        SetObjectField(f.Queue, "_consecutiveFailures", 3);
        var waiting = Signal();
        using var release = new ManualResetEventSlim();
        f.Server.Setup(x => x.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("network failure"));
        f.Queue.TaskStatusChanged += (_, task) =>
        {
            if (task.Status == TransferTaskStatus.WaitForRetry)
            {
                waiting.TrySetResult(true);
                release.Wait(Token);
            }
        };
        var delayed = Task.Run(() => f.Queue.Upload(first, ct: Token), Token);
        try
        {
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => f.Queue.Upload(second, ct: Token).WaitAsync(TimeSpan.FromSeconds(5), Token));
        }
        finally
        {
            release.Set();
        }
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => delayed.WaitAsync(TimeSpan.FromSeconds(5), Token));

        f.Server.Setup(x => x.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryRecordDto?)null);
        await f.Queue.ActiveTaskAddMutex.WaitAsync(Token);
        Task<TransferTask> forced;
        try
        {
            forced = f.Queue.EnqueueUpload(first, forceResume: true, ct: Token);
            InvokeQueue(f.Queue, "MarkAllQueuedTasksAsFailed");
        }
        finally
        {
            f.Queue.ActiveTaskAddMutex.Release();
        }
        var registered = await forced;
        Assert.AreEqual(TransferTaskStatus.Completed,
            await registered.CompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5), Token));
    }

    [TestMethod]
    [DataRow("text", false)]
    [DataRow("file", false)]
    [DataRow("size", false)]
    [DataRow("filter", false)]
    [DataRow("text", true)]
    public async Task BackgroundMetadataSync_RespectsFiltersBeforeContentFallback(string rule, bool remoteExists)
    {
        await using var f = await Fixture.CreateAsync(Token);
        Profile profile;
        if (rule == "text")
            profile = new TextProfile("filtered content");
        else
        {
            var path = Path.Combine(f.GetPersistentDir(), "private.txt");
            await File.WriteAllTextAsync(path, "filtered content", Token);
            profile = new FileProfile(path);
        }
        await f.Manager.AddLocalProfile(profile, token: Token);
        var record = await f.Db.HistoryRecords.SingleAsync(Token);
        record.Version = 2;
        record.Stared = true;
        record.SyncStatus = HistorySyncStatus.NeedSync;
        await f.Db.SaveChangesAsync(Token);
        if (remoteExists)
        {
            f.RemoteRecord = record.ToHistoryRecordDto();
            f.RemoteRecord.Version = 1;
        }
        f.UserConfig.SetConfig(new SyncConfig
        {
            EnableUploadText = rule != "text",
            EnableUploadSingleFile = rule != "file",
            MaxFileByte = rule == "size" ? 1u : 20u * 1024 * 1024
        });
        if (rule == "filter")
            f.UserConfig.SetConfig(new FileFilterConfig { FileFilterMode = "WhiteList" });
        await f.CreateSyncer().SyncOneAsync(record, Token);
        await f.Queue.WaitAllTasks(Token).WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.AreEqual(0, f.Uploads);
        Assert.AreEqual(remoteExists ? 1 : 0, f.Updates);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(remoteExists ? HistorySyncStatus.Synced : HistorySyncStatus.LocalOnly, saved.SyncStatus);
        Assert.IsTrue(saved.Stared);
        Assert.IsTrue(saved.IsLocalFileReady);
    }

    [TestMethod]
    public async Task PulledSyncedRecord_DoesNotScheduleAnUpload()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var runtime = new ConfigBase(Path.Combine(f.GetPersistentDir(), "runtime-pull.json"));
        runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = true });
        using var session = new HistorySyncSession(f.Factory, runtime);
        _ = new HistorySyncer(f.Manager, f.Factory, Mock.Of<ILogger>(), f.Queue, f.UserConfig, runtime, session);
        var remote = new HistoryRecordDto
        {
            Type = ProfileType.Text,
            Text = "remote addition",
            Hash = new string('B', 64),
            Version = 1,
            CreateTime = DateTimeOffset.UtcNow,
            LastModified = DateTimeOffset.UtcNow,
            LastAccessed = DateTimeOffset.UtcNow
        };
        await f.Manager.SyncRemoteHistoryAsync([remote], Token);
        await f.Queue.WaitAllTasks(Token).WaitAsync(TimeSpan.FromSeconds(5), Token);
        f.Server.Verify(x => x.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.AreEqual(0, f.Uploads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LocalUpload_CompletesWhenQueuedOrMerged(bool merge)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("confirmed filtered upload");
        await f.Manager.AddLocalProfile(profile, token: Token);
        f.UserConfig.SetConfig(new SyncConfig { EnableUploadText = false });
        var entered = Signal();
        var release = Signal();
        f.Server.Setup(x => x.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(Token);
                return f.RemoteRecord;
            });
        TransferTask? original = null;
        if (merge)
        {
            original = await f.Queue.EnqueueUpload(profile, ct: Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        }
        var confirmed = await f.Queue.EnqueueUpload(profile, forceResume: false, ct: Token);
        if (merge)
            Assert.AreSame(original, confirmed);
        release.TrySetResult(true);
        Assert.AreEqual(TransferTaskStatus.Completed,
            await confirmed.CompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5), Token));
        Assert.AreEqual(1, f.Uploads);
        Assert.AreEqual(HistorySyncStatus.Synced, (await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token)).SyncStatus);
    }

    [TestMethod]
    public async Task PromotedPendingUpload_SurvivesFailureSweepBeforeExecution()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("promotion before execution claim");
        await f.Manager.AddLocalProfile(profile, token: Token);
        SetObjectField(f.Queue, "_isQueueStopped", true);
        var queued = await f.Queue.EnqueueUpload(profile, ct: Token);
        var registration = typeof(HistoryTransferQueue).GetMethod("GetOrAddTaskAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var promoted = await (Task<TransferTask>)registration.Invoke(f.Queue,
            [TransferType.Upload, profile, true, null, Token])!;
        Assert.AreSame(queued, promoted);
        InvokeQueue(f.Queue, "MarkAllQueuedTasksAsFailed");
        await f.Queue.Upload(profile, ct: Token).WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.AreEqual(TransferTaskStatus.Completed, await promoted.CompletionSource.Task.WaitAsync(Token));
        Assert.AreEqual(1, f.Uploads);
    }

    [TestMethod]
    [DataRow("starred", -1)]
    [DataRow("pinned", 0)]
    [DataRow("deleted", 1)]
    public async Task RemoteMerge_UsesServerPropertiesWhenVersionsTie(string property, int minutes)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("equal version edit");
        await f.Manager.AddLocalProfile(profile, token: Token);
        var record = await f.Db.HistoryRecords.SingleAsync(Token);
        record.Version = 10;
        f.RemoteRecord = record.ToHistoryRecordDto();
        record.Stared = property == "starred";
        record.Pinned = property == "pinned";
        record.IsDeleted = property == "deleted";
        record.LastModified = record.LastModified.AddMinutes(minutes);
        record.SyncStatus = HistorySyncStatus.NeedSync;
        await f.Db.SaveChangesAsync(Token);
        await f.Manager.SyncRemoteHistoryAsync([f.RemoteRecord], Token);
        await f.CreateSyncer().SyncNeedUpdateAsync(Token);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(f.RemoteRecord.Starred, saved.Stared);
        Assert.AreEqual(f.RemoteRecord.Pinned, saved.Pinned);
        Assert.AreEqual(f.RemoteRecord.IsDeleted, saved.IsDeleted);
        Assert.AreEqual(0, f.Updates);
        Assert.AreEqual(0, f.Uploads);
        Assert.AreEqual(HistorySyncStatus.Synced, saved.SyncStatus);
    }

    private static void InvokeQueue(HistoryTransferQueue queue, string method) =>
        typeof(HistoryTransferQueue).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(queue, null);

    private async Task WaitForDisposedAsync(CancellationTokenSource source)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                _ = source.Token;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            await Task.Delay(10, Token);
        }
        Assert.Fail("The terminated transfer did not release its cancellation source.");
    }

    private sealed class MemoryHistoryDbContext : HistoryDbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=:memory:");
    }

    private sealed class Fixture : IProfileEnv, IAsyncDisposable
    {
        private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("SyncClipboard-Retention-");
        private readonly SemaphoreSlim semaphore = new(1, 1);
        private readonly ScopeGuard application;
        public MemoryHistoryDbContext Db { get; } = new();
        public HistoryConfig Config { get; } = new() { HistoryRetentionMinutes = 60, MaxItemCount = 0 };
        public HistoryManager Manager { get; }
        public RuntimeHistoryConfig RuntimeConfig { get; } = new();
        public Mock<IRemoteClipboardServer> Remote { get; } = new();
        public Mock<IOfficialSyncServer> Server => Remote.As<IOfficialSyncServer>();
        public HistoryRecordDto? RemoteRecord { get; set; }
        public RemoteClipboardServerFactory Factory { get; }
        public HistoryTransferQueue Queue { get; }
        public int Uploads;
        public int Updates;
        public ConfigManager UserConfig { get; }
        private ConfigBase Runtime { get; }
        private HistorySyncSession Session { get; }

        private Fixture()
        {
            // Exercise real cleanup against SQLite without opening the application database or starting config listeners.
            Manager = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
            SetField("_dbContext", Db);
            SetField("_dbSemaphore", semaphore);
            SetField("_profileEnv", this);
            SetField("_logger", Mock.Of<ILogger>());
            SetField("_historyConfig", Config);
            SetField("_runtimeHistoryConfig", RuntimeConfig);
            SetField("_historyManagerHelper", new HistoryManagerHelper<HistoryRecord, DateTime>(Manager));
            Manager.EnableCleanup = false;
            _ = Server;
            Factory = (RemoteClipboardServerFactory)RuntimeHelpers.GetUninitializedObject(typeof(RemoteClipboardServerFactory));
            SetObjectField(Factory, "_current", Remote.Object);
            SetObjectField(Factory, "_serverStateLock", RuntimeHelpers.GetUninitializedObject(typeof(Lock)));
            Server.Setup(x => x.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(RemoteRecord));
            Server.Setup(x => x.UploadHistoryAsync(It.IsAny<HistoryRecordDto>(), It.IsAny<FileHashInfo?>(),
                It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
                .Returns<HistoryRecordDto, FileHashInfo?, IProgress<HttpDownloadProgress>?, CancellationToken>((dto, _, _, _) =>
                {
                    Interlocked.Increment(ref Uploads);
                    RemoteRecord = dto;
                    return Task.FromResult(dto);
                });
            Server.Setup(x => x.UpdateHistoryAsync(It.IsAny<ProfileType>(), It.IsAny<string>(),
                It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
                .Returns<ProfileType, string, HistoryRecordUpdateDto, CancellationToken>((_, _, dto, _) =>
                {
                    if (RemoteRecord is null)
                        throw new RemoteHistoryNotFoundException("missing");
                    Interlocked.Increment(ref Updates);
                    RemoteRecord.Version = dto.Version!.Value;
                    RemoteRecord.LastModified = dto.LastModified!.Value;
                    RemoteRecord.LastAccessed = dto.LastAccessed!.Value;
                    RemoteRecord.Starred = dto.Starred!.Value;
                    RemoteRecord.Pinned = dto.Pinned!.Value;
                    RemoteRecord.IsDeleted = dto.IsDelete ?? RemoteRecord.IsDeleted;
                    return Task.CompletedTask;
                });
            var constructor = typeof(ConfigManager).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(c => c.GetParameters().Length == 2 && c.GetParameters()[0].ParameterType != typeof(string));
            UserConfig = (ConfigManager)constructor.Invoke([null, null]);
            typeof(ConfigBase).GetProperty(nameof(ConfigBase.Path))!.SetValue(UserConfig, Path.Combine(root.FullName, "config.json"));
            Runtime = new ConfigBase(Path.Combine(root.FullName, "runtime.json"));
            Runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = true });
            Session = new HistorySyncSession(Factory, Runtime);
            Queue = new HistoryTransferQueue(Mock.Of<ILogger>(), Factory, Manager, this, UserConfig, Runtime);
            application = UseApplication();
        }

        public HistorySyncer CreateSyncer() =>
            new(Manager, Factory, Mock.Of<ILogger>(), Queue, UserConfig, Runtime, Session);

        public ScopeGuard UseApplication()
        {
            var previous = AppCore.TryGetCurrent();
            var services = new ServiceCollection().AddSingleton<IProfileEnv>(this).AddSingleton(UserConfig)
                .AddSingleton(Mock.Of<IThreadDispatcher>()).BuildServiceProvider();
            var app = (AppCore)RuntimeHelpers.GetUninitializedObject(typeof(AppCore));
            SetObjectField(app, "<Services>k__BackingField", services);
            SetObjectField(app, "<ConfigManager>k__BackingField", UserConfig);
            var current = typeof(AppCore).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
            current.SetValue(null, app);
            return new ScopeGuard(() =>
            {
                current.SetValue(null, previous);
                services.Dispose();
            });
        }

        private void SetField(string name, object value) => typeof(HistoryManager)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Manager, value);

        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var fixture = new Fixture();
            await fixture.Db.Database.OpenConnectionAsync(token);
            await fixture.Db.Database.EnsureCreatedAsync(token);
            return fixture;
        }

        public string GetPersistentDir() => root.FullName;
        public string GetHistoryPersistentDir() => root.FullName;

        public async ValueTask DisposeAsync()
        {
            ((IDisposable)Session).Dispose();
            Queue.ClearQueue();
            Queue.Dispose();
            application.Dispose();
            await Db.DisposeAsync();
            semaphore.Dispose();
            root.Delete(true);
        }
    }
}
