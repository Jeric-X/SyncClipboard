using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Server.Core.Models;
using Moq;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.History.HistoryExport;
using SyncClipboard.Core.Utilities.History.HistoryImport;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SyncClipboard.Test;

[TestClass]
[DoNotParallelize]
public class HistoryImportTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    private sealed class MemoryDb : HistoryDbContext
    {
        public Func<Task>? BeforeSave { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=:memory:");

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (BeforeSave is { } beforeSave)
                await beforeSave();
            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class Fixture : IProfileEnv, IAsyncDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("SyncClipboard-Import-");
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly ConfigurationTestServices _configurationServices = new();
        public ConfigManager Config { get; }
        public MemoryDb Db { get; } = new();
        public SemaphoreSlim DbSemaphore => _semaphore;
        public HistoryManager Manager { get; }
        public HistoryImporter Importer { get; }
        public string Root => _root.FullName;
        public string GetPersistentDir() => Path.Combine(Root, "source");
        public Exception? StorageError { get; set; }
        public string GetHistoryPersistentDir() => StorageError is { } error
            ? throw error : Path.Combine(Root, "restored");

        public Fixture()
        {
            Manager = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
            SetField("_dbContext", Db);
            SetField("_dbSemaphore", _semaphore);
            SetField("_logger", Mock.Of<ILogger>());
            SetField("_profileEnv", this);
            Config = new ConfigManager(Path.Combine(Root, "SyncClipboard.json"), _configurationServices.Upgrader);
            Importer = new(this, Manager, Config);
        }

        private void SetField(string name, object value) => typeof(HistoryManager)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Manager, value);

        public async Task InitializeAsync(CancellationToken token)
        {
            await Db.Database.OpenConnectionAsync(token);
            await Db.Database.EnsureCreatedAsync(token);
        }

        public async Task<HistoryExportRecord> RecordAsync(Profile profile, CancellationToken token)
        {
            var content = await profile.Persist(GetPersistentDir(), token);
            var stamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            return HistoryExportRecord.FromRecord(new HistoryRecord
            {
                Type = content.Type,
                Hash = content.Hash,
                Text = content.Text,
                Size = content.Size,
                FilePath = content.FilePaths.Select(path => Profile.GetFullPath(GetPersistentDir(), content.Type, content.Hash, path)).ToArray(),
                TransferDataFile = Profile.GetFullPath(GetPersistentDir(), content.Type, content.Hash, content.TransferDataFile),
                TransferDataHash = content.TransferDataHash,
                Timestamp = stamp,
                LastModified = stamp.AddHours(1),
                LastAccessed = stamp.AddHours(2),
                Stared = true,
                Pinned = true,
                From = "backup device"
            });
        }

        public async Task<string> ExportAsync(HistoryExportRecord[] records, CancellationToken token)
        {
            var exporter = new HistoryExporter(this);
            var plan = await exporter.EstimateAsync(records, null, token);
            Assert.IsEmpty(plan.Skipped);
            var output = await exporter.ExportAsync(plan, Root, null, token);
            Assert.IsNull(output.Error);
            return output.ArchivePath!;
        }

        public async Task<HistoryImportResult> ImportAsync(string path, CancellationToken token, IProgress<int>? progress = null)
        {
            using var plan = await HistoryImporter.PrepareAsync(path, token);
            return await Importer.ImportAsync(plan, progress, token);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _semaphore.Dispose();
            _configurationServices.Dispose();
            _root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ExportThenImport_RestoresAllTypesAttachmentsAndMetadata()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var file = Path.Combine(f.Root, "sample.png");
        await File.WriteAllTextAsync(file, "file contents", Token);
        var folder = Directory.CreateDirectory(Path.Combine(f.Root, "folder")).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "nested.txt"), "nested contents", Token);
        Profile[] profiles = [new TextProfile("short text"), new TextProfile(new string('文', 12000)),
            new FileProfile(file), new ImageProfile(file), new GroupProfile([folder, file])];
        var records = new List<HistoryExportRecord>();
        foreach (var profile in profiles)
            records.Add(await f.RecordAsync(profile, Token));
        var backup = await f.ExportAsync([.. records], Token);
        var uiNotifications = 0;
        var syncNotifications = new List<HistoryRecord>();
        f.Manager.HistoryImported += () => uiNotifications++;
        f.Manager.HistoryAdded += (record, source) => syncNotifications.Add(record);
        f.Manager.HistoryUpdated += (record, source) => syncNotifications.Add(record);
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(0, result.FailureCount);
        Assert.AreEqual(5, result.ImportedCount);
        Assert.AreEqual(1, uiNotifications);
        Assert.HasCount(5, syncNotifications);
        Assert.IsNull(result.ReportPath);
        File.Delete(file);
        Directory.Delete(folder, true);
        Directory.Delete(f.GetPersistentDir(), true);
        foreach (var original in records)
        {
            var restored = await f.Db.HistoryRecords.SingleAsync(x => x.Type == original.ProfileType && x.Hash == original.Hash, Token);
            Assert.AreEqual(original.Size, restored.Size);
            Assert.AreEqual(original.Text, restored.Text);
            Assert.AreEqual(original.Timestamp, restored.Timestamp.ToUniversalTime());

            Assert.IsTrue(restored.Stared && restored.Pinned);
            Assert.AreEqual(original.From, restored.From);
            Assert.AreEqual(HistorySyncStatus.LocalOnly, restored.SyncStatus);
            var profile = Profile.Create(f.GetHistoryPersistentDir(), new ProfilePersistentInfo
            {
                Type = restored.Type,
                Hash = restored.Hash,
                Text = restored.Text,
                Size = restored.Size,
                FilePaths = restored.FilePath,
                TransferDataFile = restored.TransferDataFile,
                TransferDataHash = restored.TransferDataHash
            });
            Assert.IsTrue(await profile.IsDataComplete(false, Token), restored.Type.ToString());
        }
        using var again = await HistoryImporter.PrepareAsync(backup, Token);
        var duplicate = await f.Importer.ImportAsync(again, null, Token);
        Assert.AreEqual(5, duplicate.ExistingCount);
        Assert.AreEqual(0, duplicate.ImportedCount);
        Assert.HasCount(5, await f.Db.HistoryRecords.ToListAsync(Token));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ImportExistingRecord_RefreshesActivityAndRestoresDeletion(bool deleted)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var item = await f.RecordAsync(new TextProfile("existing content"), Token);
        var backup = await f.ExportAsync([item], Token);
        var oldTime = DateTime.UtcNow.AddDays(-90);
        var existing = new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = item.Hash,
            Text = "local preview",
            From = "local device",
            Timestamp = oldTime,
            LastAccessed = oldTime,
            LastModified = oldTime,
            IsDeleted = deleted,
            SyncStatus = HistorySyncStatus.Synced,
            Version = 4
        };
        f.Db.HistoryRecords.Add(existing);
        await f.Db.SaveChangesAsync(Token);
        var notified = false;
        f.Manager.HistoryUpdated += (record, source) =>
        {
            Assert.AreEqual(HistoryChangeSource.Import, source);
            Assert.IsTrue(f.DbSemaphore.Wait(0, TestContext.CancellationTokenSource.Token), "Notifications must run outside the database lock.");
            f.DbSemaphore.Release();
            notified = true;
        };
        var before = DateTime.UtcNow;
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(0, result.FailureCount);
        Assert.AreEqual(1, result.ExistingCount);
        Assert.IsTrue(notified);
        Assert.IsFalse(existing.IsDeleted);
        Assert.IsTrue(existing.IsLocalFileReady);
        Assert.AreEqual(oldTime, existing.Timestamp);
        Assert.IsTrue(existing.LastAccessed >= before && existing.LastAccessed <= DateTime.UtcNow);
        Assert.AreEqual(existing.LastAccessed, existing.LastModified);
        Assert.AreEqual(5, existing.Version);
        Assert.AreEqual(HistorySyncStatus.NeedSync, existing.SyncStatus);
        Assert.AreEqual("local preview", existing.Text);
        Assert.AreEqual("local device", existing.From);
        Assert.IsFalse(existing.Stared || existing.Pinned);
    }

    [TestMethod]
    public async Task ImportEvents_AreDeliveredAfterEachCommitAndSurviveObserverFailure()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var records = new[] { await f.RecordAsync(new TextProfile("one"), Token), await f.RecordAsync(new TextProfile("two"), Token) };
        var backup = await f.ExportAsync(records, Token);
        var notifications = 0;
        var before = DateTime.UtcNow;
        f.Manager.HistoryAdded += (_, _) => throw new InvalidOperationException("observer failure");
        f.Manager.HistoryAdded += (record, source) =>
        {
            Assert.AreEqual(HistoryChangeSource.Import, source);
            Assert.IsTrue(f.DbSemaphore.Wait(0, TestContext.CancellationTokenSource.Token));
            f.DbSemaphore.Release();
            Assert.IsTrue(record.LastAccessed >= before);
            Assert.AreEqual(record.LastAccessed, record.LastModified);
            Assert.IsTrue(f.Db.HistoryRecords.Any(row => row.ID == record.ID));
            notifications++;
        };
        var result = await f.ImportAsync(backup, Token, new InlineProgress(count => Assert.AreEqual(count, notifications)));
        Assert.AreEqual(2, result.ImportedCount);
        Assert.AreEqual(0, result.FailureCount);
        Assert.AreEqual(2, notifications);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ImportUpload_WaitsForOrdinaryUploadThenRestoresRemoteRecord(bool cancelUpload)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var profile = new TextProfile("in-flight upload");
        var hash = await profile.GetHash(Token);
        var stamp = DateTime.UtcNow;
        f.Db.HistoryRecords.Add(new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = hash,
            Text = profile.DisplayText,
            LastAccessed = stamp,
            LastModified = stamp,
            Stared = true,
            Version = 3
        });
        await f.Db.SaveChangesAsync(Token);
        var runtime = new ConfigBase(Path.Combine(f.Root, "runtime.json"));
        var remote = new Mock<IRemoteClipboardServer>();
        var server = remote.As<IOfficialSyncServer>();
        server.Setup(x => x.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HistoryRecordDto { Type = ProfileType.Text, Hash = hash, IsDeleted = true, Version = 7 });
        HistoryRecordUpdateDto? update = null;
        server.Setup(x => x.UpdateHistoryAsync(ProfileType.Text, hash, It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
            .Callback<ProfileType, string, HistoryRecordUpdateDto, CancellationToken>((_, _, dto, _) => update = dto)
            .Returns(Task.CompletedTask);
        var factory = (RemoteClipboardServerFactory)RuntimeHelpers.GetUninitializedObject(typeof(RemoteClipboardServerFactory));
        SetPrivateField(factory, "_current", remote.Object);
        SetPrivateField(factory, "_serverStateLock", new Lock());
        using var queue = new HistoryTransferQueue(Mock.Of<ILogger>(), factory, f.Manager, f, f.Config, runtime);
        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Db.BeforeSave = async () =>
        {
            f.Db.BeforeSave = null;
            saving.SetResult();
            await releaseSave.Task.WaitAsync(Token);
        };
        try
        {
            var ordinary = await queue.EnqueueUpload(profile, ct: Token);
            await saving.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            var importing = queue.EnqueueUpload(profile, false, HistoryChangeSource.Import, Token);
            Assert.IsFalse(importing.IsCompleted, "An import must wait for the upload that already passed its source check.");
            if (cancelUpload)
                queue.CancelUpload(await profile.GetProfileId(Token));
            releaseSave.SetResult();
            var imported = await importing.WaitAsync(TimeSpan.FromSeconds(10), Token);
            if (cancelUpload)
            {
                Assert.AreSame(ordinary, imported);
                Assert.AreEqual(TransferTaskStatus.Cancelled, imported.Status);
                Assert.IsNull(update);
                server.Verify(x => x.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
                return;
            }
            Assert.AreNotSame(ordinary, imported);
            Assert.AreEqual(TransferTaskStatus.Completed, await imported.CompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(10), Token));
            Assert.IsNotNull(update);
            Assert.IsFalse(update.IsDelete);
            Assert.IsTrue(update.Starred);
            Assert.AreEqual(stamp, update.LastAccessed!.Value.UtcDateTime);
            Assert.AreEqual(8, update.Version);
            server.Verify(x => x.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
        finally
        {
            releaseSave.TrySetResult();
            var cts = (CancellationTokenSource)typeof(HistoryTransferQueue)
                .GetField("_globalCts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
            cts.Cancel();
            if (typeof(HistoryTransferQueue).GetField("_processingTask", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(queue) is Task processing)
                await processing.WaitAsync(TimeSpan.FromSeconds(10), Token);
        }
    }

    [TestMethod]
    [DataRow(false, false, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    [DataRow(false, false, false)]
    public async Task ImportEvent_UploadsOrUpdatesWithClipboardPullEnabled(bool remoteExists, bool localExists, bool syncEnabled)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var item = await f.RecordAsync(new TextProfile("imported content"), Token);
        var backup = await f.ExportAsync([item], Token);
        if (localExists)
        {
            f.Db.HistoryRecords.Add(new HistoryRecord
            {
                Type = item.ProfileType,
                Hash = item.Hash,
                Text = item.Text,
                IsDeleted = true,
                Version = 3,
                SyncStatus = HistorySyncStatus.Synced,
                Timestamp = item.Timestamp,
                LastModified = item.LastModified,
                LastAccessed = item.LastAccessed
            });
            await f.Db.SaveChangesAsync(Token);
        }
        f.Config.SetConfig(new SyncConfig { SyncSwitchOn = true, PullSwitchOn = true });
        var runtime = new ConfigBase(Path.Combine(f.Root, "runtime.json"));
        runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = syncEnabled });
        var remote = new Mock<IRemoteClipboardServer>();
        var server = remote.As<IOfficialSyncServer>();
        server.Setup(x => x.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteExists ? new HistoryRecordDto
            {
                Type = item.ProfileType,
                Hash = item.Hash,
                Version = 7,
                IsDeleted = true
            } : null);
        HistoryRecordUpdateDto? update = null;
        HistoryRecordDto? upload = null;
        server.Setup(x => x.UpdateHistoryAsync(item.ProfileType, item.Hash, It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
            .Callback<ProfileType, string, HistoryRecordUpdateDto, CancellationToken>((_, _, dto, _) => update = dto)
            .Returns(Task.CompletedTask);
        server.Setup(x => x.UploadHistoryAsync(It.IsAny<HistoryRecordDto>(), It.IsAny<FileHashInfo?>(),
                It.IsAny<IProgress<HttpDownloadProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<HistoryRecordDto, FileHashInfo?, IProgress<HttpDownloadProgress>, CancellationToken>((dto, _, _, _) => upload = dto)
            .Returns(Task.CompletedTask);
        var factory = (RemoteClipboardServerFactory)RuntimeHelpers.GetUninitializedObject(typeof(RemoteClipboardServerFactory));
        SetPrivateField(factory, "_current", remote.Object);
        SetPrivateField(factory, "_serverStateLock", new Lock());
        using var services = new ServiceCollection().AddSingleton(f.Config).AddSingleton<IProfileEnv>(f).BuildServiceProvider();
        var currentField = typeof(AppCore).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousApp = currentField.GetValue(null);
        var app = (AppCore)RuntimeHelpers.GetUninitializedObject(typeof(AppCore));
        SetPrivateField(app, "<Services>k__BackingField", services);
        SetPrivateField(app, "<Logger>k__BackingField", Mock.Of<ILogger>());
        using var queue = new HistoryTransferQueue(Mock.Of<ILogger>(), factory, f.Manager, f, f.Config, runtime);
        _ = new HistorySyncer(f.Manager, factory, Mock.Of<ILogger>(), queue, f.Config, runtime);
        var completed = new TaskCompletionSource<TransferTask>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.TaskStatusChanged += (_, task) =>
        {
            if (task.Status is TransferTaskStatus.Completed or TransferTaskStatus.Failed)
                completed.TrySetResult(task);
        };
        try
        {
            currentField.SetValue(null, app);
            var before = DateTime.UtcNow;
            var result = await f.ImportAsync(backup, Token);
            Assert.AreEqual(0, result.FailureCount);
            if (!syncEnabled)
            {
                Assert.IsEmpty(await queue.GetAllActiveTasks(Token));
                Assert.IsNull(upload);
                Assert.IsNull(update);
                return;
            }
            var task = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            Assert.AreEqual(TransferTaskStatus.Completed, task.Status, task.ErrorMessage);
            if (remoteExists)
            {
                Assert.IsNotNull(update);
                Assert.IsFalse(update.IsDelete);
                Assert.AreEqual(8, update.Version);
                Assert.IsTrue(update.LastAccessed >= before);
                Assert.AreEqual(update.LastModified, update.LastAccessed);
                Assert.IsNull(upload);
            }
            else
            {
                Assert.IsNotNull(upload);
                Assert.AreEqual(item.Timestamp, upload.CreateTime.UtcDateTime);
                Assert.IsTrue(upload.LastAccessed >= before);
                Assert.IsNull(update);
            }
            Assert.AreEqual(HistorySyncStatus.Synced, (await f.Db.HistoryRecords.SingleAsync(Token)).SyncStatus);
        }
        finally
        {
            var cts = (CancellationTokenSource)typeof(HistoryTransferQueue)
                .GetField("_globalCts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
            cts.Cancel();
            if (typeof(HistoryTransferQueue).GetField("_processingTask", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(queue) is Task processing)
                await processing.WaitAsync(TimeSpan.FromSeconds(10), Token);
            currentField.SetValue(null, previousApp);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OrdinaryPropertySync_DoesNotRestoreRemoteDeletion(bool deleted)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var record = new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = new string('A', 64),
            Text = "local text",
            Stared = true,
            Pinned = true,
            IsDeleted = deleted,
            Version = 8,
            SyncStatus = HistorySyncStatus.NeedSync
        };
        f.Db.HistoryRecords.Add(record);
        await f.Db.SaveChangesAsync(Token);
        var remote = new Mock<IRemoteClipboardServer>();
        var server = remote.As<IOfficialSyncServer>();
        HistoryRecordUpdateDto? sent = null;
        var remoteDeleted = true;
        server.Setup(x => x.UpdateHistoryAsync(record.Type, record.Hash, It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
            .Callback<ProfileType, string, HistoryRecordUpdateDto, CancellationToken>((_, _, dto, _) =>
            {
                sent = dto;
                if (dto.IsDelete.HasValue)
                    remoteDeleted = dto.IsDelete.Value;
            }).Returns(Task.CompletedTask);
        var factory = (RemoteClipboardServerFactory)RuntimeHelpers.GetUninitializedObject(typeof(RemoteClipboardServerFactory));
        SetPrivateField(factory, "_current", remote.Object);
        SetPrivateField(factory, "_serverStateLock", new Lock());
        var syncer = (HistorySyncer)RuntimeHelpers.GetUninitializedObject(typeof(HistorySyncer));
        SetPrivateField(syncer, "_historyManager", f.Manager);
        SetPrivateField(syncer, "_remoteServerFactory", factory);
        SetPrivateField(syncer, "_logger", Mock.Of<ILogger>());

        await syncer.SyncOneAsync(record, Token);

        Assert.IsNotNull(sent);
        Assert.IsTrue(sent.Starred);
        Assert.IsTrue(sent.Pinned);
        Assert.AreEqual<bool?>(deleted ? true : null, sent.IsDelete);
        Assert.IsTrue(remoteDeleted);
        Assert.AreEqual(HistorySyncStatus.Synced, record.SyncStatus);
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }

    [TestMethod]
    public async Task InlineJson_PreservesExistingContent()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var item = await f.RecordAsync(new TextProfile("existing"), Token);
        var backup = await f.ExportAsync([item], Token);
        var existing = new HistoryRecord { Type = ProfileType.Text, Hash = item.Hash.ToLowerInvariant(), Text = "local", IsDeleted = true };
        f.Db.HistoryRecords.Add(existing);
        await f.Db.SaveChangesAsync(Token);
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(1, result.ExistingCount);
        Assert.AreEqual("local", existing.Text);
        Assert.AreEqual(0, result.FailureCount);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("hash")]
    [DataRow("size")]
    [DataRow("path")]
    [DataRow("name")]
    [DataRow("duplicate-entry")]
    [DataRow("missing", "empty")]
    [DataRow("missing", "nonempty")]
    [DataRow("hash", "concurrent")]
    public async Task InvalidAttachment_IsReportedAndOtherRecordsStillImport(string mode, string existingRoot = "")
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var file = Path.Combine(f.Root, "data.txt");
        await File.WriteAllTextAsync(file, "payload", Token);
        var item = await f.RecordAsync(new FileProfile(file), Token);
        var inline = await f.RecordAsync(new TextProfile("valid"), Token);
        var backup = await f.ExportAsync([item, inline], Token);
        using (var archive = ZipFile.Open(backup, ZipArchiveMode.Update))
        {
            var manifest = archive.GetEntry("history.json")!;
            HistoryExportDocument document;
            using (var stream = manifest.Open())
                document = (await JsonSerializer.DeserializeAsync<HistoryExportDocument>(stream, HistoryExporter.JsonOptions, Token))!;
            var exported = document.Records.Single(x => x.HasTransferData);
            var transfer = exported.TransferData!;
            switch (mode)
            {
                case "missing": archive.GetEntry(transfer.Path)!.Delete(); break;
                case "hash": transfer = transfer with { Sha256 = new string('0', 64) }; break;
                case "size": transfer = transfer with { Size = transfer.Size + 1 }; break;
                case "path": transfer = transfer with { Path = "../escape.txt" }; break;
                case "name": transfer = transfer with { Name = "../escape.txt" }; break;
                case "duplicate-entry": archive.CreateEntry(transfer.Path); break;
            }
            document = document with { Records = document.Records.Select(x => x == exported ? x with { TransferData = transfer } : x).ToArray() };
            manifest.Delete();
            await using var output = archive.CreateEntry("history.json").Open();
            await JsonSerializer.SerializeAsync(output, document, HistoryExporter.JsonOptions, Token);
        }
        var recordFolder = Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), item.ProfileType, item.Hash);
        if (existingRoot is "empty" or "nonempty")
            Directory.CreateDirectory(recordFolder);
        var existingFile = Path.Combine(recordFolder, "existing.txt");
        if (existingRoot == "nonempty")
            await File.WriteAllTextAsync(existingFile, "keep", Token);
        using var plan = await HistoryImporter.PrepareAsync(backup, Token);
        var importer = existingRoot == "concurrent" ? new HistoryImporter(f, f.Manager, f.Config, _ =>
        {
            File.WriteAllText(existingFile, "keep");
            return long.MaxValue;
        }) : f.Importer;
        var result = await importer.ImportAsync(plan, null, Token);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.AreEqual(1, result.FailureCount);
        Assert.IsNotNull(result.ReportPath);
        Assert.Contains(item.Hash, await File.ReadAllTextAsync(result.ReportPath, Token));
        Assert.IsFalse(File.Exists(Path.Combine(f.GetHistoryPersistentDir(), "escape.txt")));
        Assert.AreEqual(existingRoot.Length != 0, Directory.Exists(recordFolder));
        if (existingRoot is "nonempty" or "concurrent")
            Assert.AreEqual("keep", await File.ReadAllTextAsync(existingFile, Token));
        else if (existingRoot == "empty")
            Assert.IsEmpty(Directory.GetFileSystemEntries(recordFolder));
    }

    [TestMethod]
    public async Task Cancellation_KeepsAlreadyCommittedRecords()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var backup = await f.ExportAsync([await f.RecordAsync(new TextProfile("one"), Token), await f.RecordAsync(new TextProfile("two"), Token)], Token);
        var notifications = 0;
        f.Manager.HistoryImported += () => notifications++;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var result = await f.ImportAsync(backup, cancellation.Token, new InlineProgress(_ =>
        {
            Assert.AreEqual(0, notifications);
            cancellation.Cancel();
        }));
        Assert.IsTrue(result.Canceled);
        Assert.AreEqual(1, notifications);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.HasCount(1, await f.Db.HistoryRecords.ToListAsync(Token));
    }

    [TestMethod]
    [DataRow("format")]
    [DataRow("version")]
    [DataRow("records")]
    public async Task UnsupportedManifest_IsRejectedBeforeWritingRecords(string mode)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var document = new HistoryExportDocument(mode == "format" ? "other" : "syncclipboard-history", mode == "version" ? 2 : 1,
            "test", DateTime.UtcNow, mode == "records" ? null! : []);
        var path = Path.Combine(f.Root, "invalid.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, HistoryExporter.JsonOptions), Token);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HistoryImporter.PrepareAsync(path, Token));
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
    }

    [TestMethod]
    [DataRow("timestamp", "missing")]
    [DataRow("lastModified", "missing")]
    [DataRow("lastAccessed", "missing")]
    [DataRow("timestamp", "min")]
    [DataRow("lastModified", "min")]
    [DataRow("lastAccessed", "min")]
    [DataRow("timestamp", "max")]
    [DataRow("lastModified", "max")]
    [DataRow("lastAccessed", "max")]
    public async Task MissingOrInvalidTimestamp_IsReportedAndOtherRecordsStillImport(string field, string mode)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var invalid = await f.RecordAsync(new TextProfile("invalid metadata"), Token);
        var valid = await f.RecordAsync(new TextProfile("valid metadata"), Token);
        var document = new HistoryExportDocument("syncclipboard-history", 1, "test", DateTime.UtcNow, [invalid, valid]);
        var json = JsonSerializer.SerializeToNode(document, HistoryExporter.JsonOptions)!;
        var record = json["records"]![0]!.AsObject();
        if (mode == "missing")
            Assert.IsTrue(record.Remove(field));
        else
            record[field] = JsonValue.Create(mode == "min" ? DateTime.MinValue : DateTime.MaxValue);
        var path = Path.Combine(f.Root, "invalid.json");
        await File.WriteAllTextAsync(path, json.ToJsonString(), Token);
        var result = await f.ImportAsync(path, Token);
        Assert.AreEqual(1, result.FailureCount);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.IsNotNull(result.ReportPath);
        Assert.Contains("timestamp", await File.ReadAllTextAsync(result.ReportPath, Token));
        var restored = await f.Db.HistoryRecords.SingleAsync(Token);
        Assert.AreEqual(valid.Hash, restored.Hash);
        Assert.AreEqual(valid.Timestamp, restored.Timestamp.ToUniversalTime());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NullManifestRecord_IsReportedAndOtherRecordsStillImport(bool zip)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var valid = await f.RecordAsync(new TextProfile("valid content"), Token);
        var document = new HistoryExportDocument("syncclipboard-history", 1, "test", DateTime.UtcNow, [null!, valid]);
        var path = Path.Combine(f.Root, zip ? "backup.zip" : "backup.json");
        if (zip)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            await using var output = archive.CreateEntry("history.json").Open();
            await JsonSerializer.SerializeAsync(output, document, HistoryExporter.JsonOptions, Token);
        }
        else
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, HistoryExporter.JsonOptions), Token);
        }
        var result = await f.ImportAsync(path, Token);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.AreEqual(1, result.FailureCount);
        Assert.AreEqual(valid.Hash, (await f.Db.HistoryRecords.SingleAsync(Token)).Hash);
        Assert.IsNotNull(result.ReportPath);
        Assert.Contains("1. -: Unsupported record type.", await File.ReadAllTextAsync(result.ReportPath, Token));
    }

    [TestMethod]
    public async Task ManyFailures_AreWrittenDuringImportWithAllDetailsAndFinalCounts()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        const int failureCount = 10000;
        var valid = await f.RecordAsync(new TextProfile("valid after failures"), Token);
        var records = new HistoryExportRecord[failureCount + 1];
        records[^1] = valid;
        var document = new HistoryExportDocument("syncclipboard-history", 1, "test", DateTime.UtcNow, records);
        var path = Path.Combine(f.Root, "backup.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, HistoryExporter.JsonOptions), Token);
        var result = await f.ImportAsync(path, Token, new InlineProgress(count =>
        {
            if (count != 1 && count != failureCount)
                return;
            var report = Directory.GetFiles(f.Root, "*-not-imported-*.txt").Single();
            using var reader = new StreamReader(new FileStream(report, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            Assert.Contains($"{count}. -: Unsupported record type.", reader.ReadToEnd());
        }));
        Assert.AreEqual(failureCount, result.FailureCount);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.IsNull(result.ReportError);
        Assert.IsNotNull(result.ReportPath);
        var lines = await File.ReadAllLinesAsync(result.ReportPath, Token);
        for (var i = 0; i < failureCount; i++)
            Assert.AreEqual($"{i + 1}. -: Unsupported record type.", lines[i + 2]);
        Assert.AreEqual(HistoryImportReportWriter.FormatResult(result), lines[^1]);
        Assert.AreEqual(valid.Hash, (await f.Db.HistoryRecords.SingleAsync(Token)).Hash);
    }

    [TestMethod]
    public async Task ReportCreationFailure_StopsImportAndPreservesCommittedRecords()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var first = await f.RecordAsync(new TextProfile("committed before report error"), Token);
        var later = await f.RecordAsync(new TextProfile("not processed"), Token);
        var backup = await f.ExportAsync([first], Token);
        var document = new HistoryExportDocument("syncclipboard-history", 1, "test", DateTime.UtcNow, [first, null!, later]);
        using var plan = new HistoryImportPlan(Path.Combine(f.Root, "unavailable", "backup.json"),
            File.OpenRead(backup), null, document, Token);
        var result = await f.Importer.ImportAsync(plan, null, Token);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.AreEqual(1, result.FailureCount);
        Assert.IsFalse(result.Canceled);
        Assert.IsNotNull(result.ReportError);
        Assert.Contains("2. -: Unsupported record type.", result.ReportError);
        Assert.IsNull(result.ReportPath);
        Assert.AreEqual(first.Hash, (await f.Db.HistoryRecords.SingleAsync(Token)).Hash);
    }

    [TestMethod]
    public async Task InvalidInlineHash_IsNotPersisted()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var item = (await f.RecordAsync(new TextProfile("valid"), Token)) with { Hash = new string('0', 64) };
        var path = Path.Combine(f.Root, "invalid.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new HistoryExportDocument("syncclipboard-history", 1, "test", DateTime.UtcNow, [item]), HistoryExporter.JsonOptions), Token);
        var result = await f.ImportAsync(path, Token);
        Assert.AreEqual(1, result.FailureCount);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingAttachment_IsRepairedWithoutChangingLocalProperties(bool deleted)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var file = Path.Combine(f.Root, "data.txt");
        await File.WriteAllTextAsync(file, "payload", Token);
        var item = await f.RecordAsync(new FileProfile(file), Token);
        var backup = await f.ExportAsync([item], Token);
        var local = new HistoryRecord
        {
            Type = item.ProfileType,
            Hash = item.Hash.ToLowerInvariant(),
            Text = "local text",
            Size = item.Size,
            FilePath = ["missing.txt"],
            IsLocalFileReady = false,
            Stared = false,
            Pinned = false,
            From = "local device",
            Timestamp = DateTime.UtcNow.AddDays(-2),
            LastModified = DateTime.UtcNow.AddDays(-1),
            LastAccessed = DateTime.UtcNow,
            SyncStatus = HistorySyncStatus.Synced,
            Version = 42,
            IsDeleted = deleted
        };
        f.Db.HistoryRecords.Add(local);
        await f.Db.SaveChangesAsync(Token);
        var (Timestamp, LastModified, LastAccessed) = (local.Timestamp, local.LastModified, local.LastAccessed);
        var uiNotifications = 0;
        var syncNotifications = new List<HistoryRecord>();
        f.Manager.HistoryImported += () => uiNotifications++;
        f.Manager.HistoryUpdated += (record, source) => syncNotifications.Add(record);
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(1, result.RepairedCount);
        Assert.AreEqual(1, uiNotifications);
        Assert.HasCount(1, syncNotifications);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.AreEqual(0, result.FailureCount);
        Assert.AreEqual(Timestamp, local.Timestamp);
        Assert.AreEqual("local text", local.Text);
        Assert.AreEqual("local device", local.From);
        Assert.IsFalse(local.Stared || local.Pinned);
        Assert.IsFalse(local.IsDeleted);
        Assert.IsTrue(local.LastAccessed > LastAccessed);
        Assert.AreEqual(local.LastAccessed, local.LastModified);
        Assert.AreEqual(43, local.Version);
        Assert.AreEqual(HistorySyncStatus.NeedSync, local.SyncStatus);
        Assert.IsTrue(local.IsLocalFileReady);
        var restored = Profile.GetFullPath(f.GetHistoryPersistentDir(), local.Type, local.Hash, local.TransferDataFile)!;
        Assert.AreEqual("payload", await File.ReadAllTextAsync(restored, Token));
        Assert.AreEqual(item.Hash.ToLowerInvariant(), local.Hash);
        await f.Manager.RemoveHistory(local, Token);
        Assert.IsFalse(File.Exists(restored));
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AttachmentDeletedWhileWaitingForCommit_IsNotReportedAsImported(bool removeRecord)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var source = Path.Combine(f.Root, "data.txt");
        await File.WriteAllTextAsync(source, "payload", Token);
        var sourceProfile = new FileProfile(source);
        var hash = await sourceProfile.GetHash(Token);
        var root = Profile.CreateWorkingDir(f.GetHistoryPersistentDir(), ProfileType.File, hash);
        var directory = Directory.CreateDirectory(Path.Combine(root, "import-staged")).FullName;
        var staged = Path.Combine(directory, "data.txt");
        File.Copy(source, staged);
        var profile = new FileProfile(staged);
        var data = await profile.Persist(f.GetHistoryPersistentDir(), Token);
        var record = new HistoryRecord
        {
            Type = ProfileType.File,
            Hash = data.Hash,
            Text = data.Text,
            Size = data.Size,
            FilePath = data.FilePaths,
            TransferDataFile = data.TransferDataFile,
            TransferDataHash = data.TransferDataHash,
            IsLocalFileReady = true
        };
        var local = new HistoryRecord { Type = ProfileType.File, Hash = hash, FilePath = ["missing.txt"], IsLocalFileReady = false };
        f.Db.HistoryRecords.Add(local);
        await f.Db.SaveChangesAsync(Token);
        Assert.IsTrue(await profile.IsDataComplete(false, Token));
        await f.DbSemaphore.WaitAsync(Token);
        Task<HistoryImportOutcome> commit;
        try
        {
            commit = f.Manager.AddImportProfile(record, directory, Token);
            Directory.Delete(root, recursive: true);
            if (removeRecord)
            {
                f.Db.HistoryRecords.Remove(local);
                await f.Db.SaveChangesAsync(Token);
            }
        }
        finally
        {
            f.DbSemaphore.Release();
        }
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => commit);
        var rows = await f.Db.HistoryRecords.ToListAsync(Token);
        Assert.AreEqual(removeRecord ? 0 : 1, rows.Count);
        Assert.IsFalse(local.IsLocalFileReady);
        Assert.IsFalse(f.Db.ChangeTracker.HasChanges());
    }

    [TestMethod]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    [TestCategory("PlatformWindows")]
    public async Task OrphanCleanupWaitsForCommitAndPreservesItsAttachments()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
            Assert.Inconclusive("Requires a supported filesystem.");
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var record = new HistoryRecord { Type = ProfileType.File, Hash = new string('A', 64) };
        var root = Profile.CreateWorkingDir(f.GetHistoryPersistentDir(), record.Type, record.Hash);
        var orphan = Directory.CreateDirectory(Path.Combine(f.GetHistoryPersistentDir(), "unused")).FullName;
        Directory.SetCreationTime(root, DateTime.Now.AddDays(-10));
        Directory.SetCreationTime(orphan, DateTime.Now.AddDays(-10));
        if (Directory.GetCreationTime(root) > DateTime.Now.AddDays(-7) || Directory.GetCreationTime(orphan) > DateTime.Now.AddDays(-7))
            Assert.Inconclusive("Filesystem cannot set directory creation time.");
        var attachment = Path.Combine(root, "data.txt");
        await File.WriteAllTextAsync(attachment, "imported data", Token);
        record.FilePath = ["data.txt"];
        await f.DbSemaphore.WaitAsync(Token);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = Task.Run(() =>
        {
            started.SetResult();
            f.Manager.CleanupOrphanedHistoryFolders(Token);
        }, Token);
        try
        {
            await started.Task.WaitAsync(Token);
            await Task.Delay(100, Token);
            Assert.IsFalse(cleanup.IsCompleted, "Cleanup must wait for the in-flight database commit.");
            f.Db.HistoryRecords.Add(record);
            await f.Db.SaveChangesAsync(Token);
        }
        finally
        {
            f.DbSemaphore.Release();
        }
        await cleanup.WaitAsync(Token);
        Assert.IsTrue(File.Exists(attachment));
        Assert.IsFalse(Directory.Exists(orphan), "Old directories without a record still need cleanup.");
    }

    [TestMethod]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    [DataRow(false, @"report\2026.txt")]
    [DataRow(true, @"report\2026.txt")]
    [DataRow(false, " ")]
    [DataRow(false, "\t")]
    [DataRow(true, " ")]
    public async Task UnixFileName_RoundTrips(bool image, string name)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            Assert.Inconclusive("Requires a Unix filesystem.");
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var file = Path.Combine(f.Root, name);
        await File.WriteAllTextAsync(file, "payload", Token);
        var original = await f.RecordAsync(image ? new ImageProfile(file) : new FileProfile(file), Token);
        var backup = await f.ExportAsync([original], Token);
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.AreEqual(0, result.FailureCount);
        var record = await f.Db.HistoryRecords.SingleAsync(Token);
        Assert.AreEqual(original.Hash, record.Hash);
        Assert.AreEqual(Path.GetFileName(file), record.Text);
        var restored = Profile.GetFullPath(f.GetHistoryPersistentDir(), record.Type, record.Hash, record.TransferDataFile)!;
        Assert.AreEqual(Path.GetFileName(file), Path.GetFileName(restored));
        Assert.AreEqual("payload", await File.ReadAllTextAsync(restored, Token));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task RepairCleansObsoleteOwnedAttachmentsOnlyAfterCommit(bool group, bool rejectCommit)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var source = Path.Combine(f.Root, "data.txt");
        await File.WriteAllTextAsync(source, "payload", Token);
        var item = await f.RecordAsync(group ? new GroupProfile([source]) : new FileProfile(source), Token);
        var backup = await f.ExportAsync([item], Token);
        var root = Profile.CreateWorkingDir(f.GetHistoryPersistentDir(), item.ProfileType, item.Hash);
        var oldDirectory = Directory.CreateDirectory(Path.Combine(root, "old")).FullName;
        var obsolete = Path.Combine(oldDirectory, "data.txt");
        await File.WriteAllTextAsync(obsolete, "corrupt", Token);
        var oldTransfer = Path.Combine(root, "corrupt-transfer.bin");
        await File.WriteAllTextAsync(oldTransfer, "corrupt archive", Token);
        var external = Path.Combine(f.Root, "external.txt");
        await File.WriteAllTextAsync(external, "preserve", Token);
        var local = new HistoryRecord
        {
            Type = item.ProfileType,
            Hash = item.Hash,
            Text = item.Text,
            Size = item.Size,
            FilePath = [group ? oldDirectory : obsolete, external],
            TransferDataFile = oldTransfer,
            IsLocalFileReady = false
        };
        f.Db.HistoryRecords.Add(local);
        await f.Db.SaveChangesAsync(Token);
        if (rejectCommit)
            await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_repair BEFORE UPDATE ON HistoryRecords BEGIN SELECT RAISE(ABORT, 'rejected'); END;", Token);
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(rejectCommit ? 0 : 1, result.RepairedCount);
        Assert.AreEqual(rejectCommit ? 1 : 0, result.FailureCount);
        Assert.AreEqual(rejectCommit, File.Exists(obsolete));
        Assert.AreEqual(rejectCommit, File.Exists(oldTransfer));
        Assert.AreEqual(rejectCommit, Directory.Exists(oldDirectory));
        Assert.AreEqual("preserve", await File.ReadAllTextAsync(external, Token));
        if (!rejectCommit)
            Assert.IsTrue(File.Exists(Profile.GetFullPath(f.GetHistoryPersistentDir(), local.Type, local.Hash, local.TransferDataFile)));
    }

    [TestMethod]
    public async Task ExistingRecordSaveFailure_IsReportedWithoutNotification()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var item = await f.RecordAsync(new TextProfile("existing"), Token);
        var backup = await f.ExportAsync([item], Token);
        var existing = new HistoryRecord
        {
            Type = item.ProfileType,
            Hash = item.Hash,
            Text = item.Text,
            IsDeleted = true,
            LastModified = item.LastModified,
            LastAccessed = item.LastAccessed,
            Version = 8,
            SyncStatus = HistorySyncStatus.Synced
        };
        f.Db.HistoryRecords.Add(existing);
        await f.Db.SaveChangesAsync(Token);
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_refresh BEFORE UPDATE ON HistoryRecords BEGIN SELECT RAISE(ABORT, 'rejected'); END;", Token);
        var notifications = 0;
        f.Manager.HistoryUpdated += (_, _) => notifications++;
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(1, result.FailureCount);
        Assert.AreEqual(0, notifications);
    }

    [TestMethod]
    public async Task DatabaseFailure_DoesNotLeaveImportedAttachments()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var file = Path.Combine(f.Root, "data.txt");
        await File.WriteAllTextAsync(file, "payload", Token);
        var item = await f.RecordAsync(new FileProfile(file), Token);
        var backup = await f.ExportAsync([item], Token);
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_import BEFORE INSERT ON HistoryRecords BEGIN SELECT RAISE(ABORT, 'rejected'); END;", Token);
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.AreEqual(1, result.FailureCount);
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
        Assert.IsFalse(Directory.Exists(Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), item.ProfileType, item.Hash)));
        Assert.IsFalse(f.Db.ChangeTracker.HasChanges());
    }

    [TestMethod]
    public async Task NotificationFailure_DoesNotRemoveCommittedAttachments()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var file = Path.Combine(f.Root, "data.txt");
        await File.WriteAllTextAsync(file, "payload", Token);
        var item = await f.RecordAsync(new FileProfile(file), Token);
        var backup = await f.ExportAsync([item], Token);
        f.Manager.HistoryAdded += (_, _) => throw new InvalidOperationException("observer failure");
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.AreEqual(0, result.FailureCount);
        var row = await f.Db.HistoryRecords.SingleAsync(Token);
        Assert.IsTrue(File.Exists(Profile.GetFullPath(f.GetHistoryPersistentDir(), row.Type, row.Hash, row.TransferDataFile)));
    }

    [TestMethod]
    public async Task GroupWithValidTransferHashStillRequiresSemanticValidation()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var file = Path.Combine(f.Root, "data.txt");
        await File.WriteAllTextAsync(file, "payload", Token);
        var item = await f.RecordAsync(new GroupProfile([file]), Token);
        var backup = await f.ExportAsync([item], Token);
        using (var archive = ZipFile.Open(backup, ZipArchiveMode.Update))
        {
            var manifest = archive.GetEntry("history.json")!;
            HistoryExportDocument document;
            using (var stream = manifest.Open())
                document = (await JsonSerializer.DeserializeAsync<HistoryExportDocument>(stream, HistoryExporter.JsonOptions, Token))!;
            var original = document.Records.Single();
            var changed = original with { Hash = new string('0', 64) };
            var path = $"files/{changed.ProfileId}/{FileSys.SafeFileName(original.TransferData!.Name)}";
            var entry = archive.GetEntry(original.TransferData.Path)!;
            using var bytes = new MemoryStream();
            using (var input = entry.Open())
                await input.CopyToAsync(bytes, Token);
            entry.Delete();
            await using (var target = archive.CreateEntry(path).Open())
                await target.WriteAsync(bytes.ToArray(), Token);
            changed = changed with { TransferData = original.TransferData with { Path = path } };
            manifest.Delete();
            await using var json = archive.CreateEntry("history.json").Open();
            await JsonSerializer.SerializeAsync(json, document with { Records = [changed] }, HistoryExporter.JsonOptions, Token);
        }
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.AreEqual(1, result.FailureCount);
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, true)]
    [DataRow(3, true)]
    public async Task ConfiguredGroupEntryLimit_SkipsOnlyOversizedGroup(int limit, bool accepted)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        Assert.AreEqual(1_000_000u, f.Config.GetConfig<HistoryImportConfig>().MaxGroupEntryCount);
        f.Config.SetConfig(new HistoryImportConfig { MaxGroupEntryCount = (uint)limit });
        f.Config.Reload();
        var first = Path.Combine(f.Root, "first.txt");
        var second = Path.Combine(f.Root, "second.txt");
        await File.WriteAllTextAsync(first, "", Token);
        await File.WriteAllTextAsync(second, "", Token);
        var group = await f.RecordAsync(new GroupProfile([first, second]), Token);
        var following = await f.RecordAsync(new TextProfile("following record"), Token);
        var backup = await f.ExportAsync([group, following], Token);
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(accepted ? 2 : 1, result.ImportedCount);
        Assert.IsNull(result.Error);
        Assert.IsTrue(await f.Db.HistoryRecords.AnyAsync(record => record.Hash == following.Hash, Token));
        if (accepted)
        {
            Assert.AreEqual(0, result.FailureCount);
            var restored = await f.Db.HistoryRecords.SingleAsync(record => record.Hash == group.Hash, Token);
            Assert.HasCount(2, restored.FilePath);
            foreach (var path in restored.FilePath)
                Assert.IsTrue(File.Exists(Profile.GetFullPath(f.GetHistoryPersistentDir(), restored.Type, restored.Hash, path)));
        }
        else
        {
            Assert.IsNotNull(result.ReportPath);
            Assert.Contains("file and directory limit", await File.ReadAllTextAsync(result.ReportPath, Token));
            AssertNoImportDirectories(Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), group.ProfileType, group.Hash));
        }
    }

    [TestMethod]
    [DataRow(false, 5, false)]
    [DataRow(false, 6, true)]
    [DataRow(true, 5, false)]
    [DataRow(true, 6, true)]
    public async Task GroupEntryLimit_CountsImplicitParentsAndEmptyDirectoriesOnce(bool explicitParents, int limit, bool accepted)
    {
        await using var f = new Fixture();
        var path = Path.Combine(f.Root, "entries.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("folder/");
            if (explicitParents)
            {
                zip.CreateEntry("folder/a/");
                zip.CreateEntry("folder/a/b/");
            }
            zip.CreateEntry("folder/a/b/first.txt");
            zip.CreateEntry("folder/a/b/second.txt");
            zip.CreateEntry("folder/empty/");
        }
        var profile = new GroupProfile(new SyncClipboard.Shared.ProfileDto());
        var file = new FileHashInfo(path, await Utility.CalculateFileSHA256(path, Token));
        if (accepted)
        {
            await profile.SetTransferData(file, 0, (uint)limit, Token);
            var folder = profile.Files.Single();
            Assert.IsTrue(File.Exists(Path.Combine(folder, "a", "b", "first.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(folder, "a", "b", "second.txt")));
            Assert.IsTrue(Directory.Exists(Path.Combine(folder, "empty")));
            Assert.AreEqual(0L, await profile.GetSize(Token));
        }
        else
        {
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => profile.SetTransferData(file, 0, (uint)limit, Token));
            Assert.IsEmpty(Directory.GetDirectories(f.Root, "entries.*"));
        }
    }

    [TestMethod]
    [DataRow("folder/../extra")]
    [DataRow("./extra")]
    [DataRow("./declared.txt")]
    [DataRow("folder/./extra")]
    [DataRow("folder/../extra/")]
    public async Task GroupExtraction_RejectsDotSegmentsEvenInsideRoot(string entryName)
    {
        await using var f = new Fixture();
        var declared = Path.Combine(f.Root, "declared.txt");
        await File.WriteAllTextAsync(declared, "", Token);
        var hash = await new GroupProfile([declared]).GetHash(Token);
        var path = Path.Combine(f.Root, "group.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntry("declared.txt");
            archive.CreateEntry(entryName);
        }
        var profile = new GroupProfile(new SyncClipboard.Shared.ProfileDto { Hash = hash });
        var transfer = new FileHashInfo(path, await Utility.CalculateFileSHA256(path, Token));
        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => profile.SetTransferData(transfer, 0, 100, Token));
        Assert.Contains("dot path segment", error.Message);
        Assert.IsEmpty(Directory.GetDirectories(f.Root));
    }

    [TestMethod]
    public async Task GroupExtraction_RejectsUnderstatedEntryLengthWhileStreaming()
    {
        await using var f = new Fixture();
        var path = Path.Combine(f.Root, "forged.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            await using var entry = zip.CreateEntry("large.bin", CompressionLevel.Optimal).Open();
            await entry.WriteAsync(new byte[1024 * 1024], Token);
        }
        var bytes = await File.ReadAllBytesAsync(path, Token);
        // Central-directory uncompressed size claims one byte; the deflate stream contains 1 MiB.
        var offset = bytes.AsSpan().IndexOf(new byte[] { 0x50, 0x4b, 0x01, 0x02 });
        Assert.IsGreaterThanOrEqualTo(0, offset);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24, 4), 1);
        await File.WriteAllBytesAsync(path, bytes, Token);
        var profile = new GroupProfile(new SyncClipboard.Shared.ProfileDto { Hash = new string('0', 64) });
        var hash = await Utility.CalculateFileSHA256(path, Token);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => profile.SetTransferData(new FileHashInfo(path, hash), 1, 1_000_000, Token));
        Assert.IsEmpty(Directory.GetDirectories(f.Root, "forged.*"));
    }

    [TestMethod]
    public async Task DiskFull_StopsAfterCurrentRecordAndPreservesCommittedRecords()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        HistoryExportRecord[] records = [await f.RecordAsync(new TextProfile("first"), Token),
            await f.RecordAsync(new TextProfile("second"), Token), await f.RecordAsync(new TextProfile("third"), Token)];
        var backup = await f.ExportAsync(records, Token);
        var error = new IOException("Disk full", unchecked((int)0x80070070));
        var result = await f.ImportAsync(backup, Token, new InlineProgress(_ => f.StorageError = error));
        Assert.AreEqual(1, result.ImportedCount);
        Assert.AreEqual("Disk full", result.Error);
        Assert.AreEqual(1, result.FailureCount);
        Assert.HasCount(1, await f.Db.HistoryRecords.ToListAsync(Token));
        Assert.IsFalse(result.Canceled);
        Assert.Contains("Disk full", await File.ReadAllTextAsync(result.ReportPath!, Token));
    }

    [TestMethod]
    [DataRow("File")]
    [DataRow("Image")]
    [DataRow("Group")]
    public async Task AlteredFileDisplayText_IsRejectedWithoutBlockingOtherRecords(string type)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var path = Path.Combine(f.Root, "data.png");
        await File.WriteAllTextAsync(path, "payload", Token);
        Profile profile = type switch
        {
            "File" => new FileProfile(path),
            "Image" => new ImageProfile(path),
            _ => new GroupProfile([path])
        };
        var item = await f.RecordAsync(profile, Token);
        var other = await f.RecordAsync(new TextProfile("valid record"), Token);
        var backup = await f.ExportAsync([item, other], Token);
        using (var zip = ZipFile.Open(backup, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("history.json")!;
            HistoryExportDocument document;
            using (var input = entry.Open())
                document = (await JsonSerializer.DeserializeAsync<HistoryExportDocument>(input, HistoryExporter.JsonOptions, Token))!;
            document = document with
            {
                Records = document.Records.Select(record => record.Hash == item.Hash ? record with { Text = "unrelated.png" } : record).ToArray()
            };
            entry.Delete();
            await using var output = zip.CreateEntry("history.json").Open();
            await JsonSerializer.SerializeAsync(output, document, HistoryExporter.JsonOptions, Token);
        }
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.Contains("display text", await File.ReadAllTextAsync(result.ReportPath!, Token));
        Assert.AreEqual(other.Hash, (await f.Db.HistoryRecords.SingleAsync(Token)).Hash);
        AssertNoImportDirectories(Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), item.ProfileType, item.Hash));
    }

    [TestMethod]
    public async Task InlineTextImport_DoesNotCreateAttachmentDirectories()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        HistoryExportRecord[] records = [await f.RecordAsync(new TextProfile("first"), Token),
            await f.RecordAsync(new TextProfile("second"), Token), await f.RecordAsync(new TextProfile("third"), Token)];
        var backup = await f.ExportAsync(records, Token);
        Assert.AreEqual(".json", Path.GetExtension(backup));
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(3, result.ImportedCount);
        Assert.AreEqual(0, result.FailureCount);
        var restored = await f.Db.HistoryRecords.ToListAsync(Token);
        CollectionAssert.AreEquivalent(records.Select(record => record.Hash).ToArray(), restored.Select(record => record.Hash).ToArray());
        Assert.IsTrue(restored.All(record => record.FilePath.Length == 0 && record.TransferDataFile is null));
        Assert.IsFalse(Directory.Exists(f.GetHistoryPersistentDir()));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task NonCanonicalTextAttachment_IsRejectedWithoutBlockingOtherRecords(bool bom)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var text = new string('文', 12000);
        var item = await f.RecordAsync(new TextProfile(text), Token);
        var other = await f.RecordAsync(new TextProfile("valid"), Token);
        var backup = await f.ExportAsync([item, other], Token);
        byte[] prefix = bom ? [0xEF, 0xBB, 0xBF] : [0xFF];
        byte[] bytes = [.. prefix, .. System.Text.Encoding.UTF8.GetBytes(text)];
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var decoded = bom ? text : "\uFFFD" + text;
        using (var zip = ZipFile.Open(backup, ZipArchiveMode.Update))
        {
            var manifest = zip.GetEntry("history.json")!;
            HistoryExportDocument document;
            using (var input = manifest.Open())
                document = (await JsonSerializer.DeserializeAsync<HistoryExportDocument>(input, HistoryExporter.JsonOptions, Token))!;
            var original = document.Records.Single(record => record.Hash == item.Hash);
            var changed = original with { Hash = hash, Text = new TextProfile(decoded).DisplayText, Size = decoded.Length };
            var transfer = original.TransferData!;
            var path = $"files/{changed.ProfileId}/{FileSys.SafeFileName(transfer.Name)}";
            zip.GetEntry(transfer.Path)!.Delete();
            await using (var output = zip.CreateEntry(path).Open())
                await output.WriteAsync(bytes, Token);
            changed = changed with { TransferData = transfer with { Path = path, Sha256 = hash, Size = bytes.Length } };
            document = document with { Records = document.Records.Select(record => record == original ? changed : record).ToArray() };
            manifest.Delete();
            await using var manifestOutput = zip.CreateEntry("history.json").Open();
            await JsonSerializer.SerializeAsync(manifestOutput, document, HistoryExporter.JsonOptions, Token);
        }
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.Contains("Decoded text", await File.ReadAllTextAsync(result.ReportPath!, Token));
        Assert.AreEqual(other.Hash, (await f.Db.HistoryRecords.SingleAsync(Token)).Hash);
        Assert.IsFalse(Directory.Exists(Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), ProfileType.Text, hash)));
    }

    [TestMethod]
    public async Task AlteredLongTextPreview_IsRejectedWithoutBlockingOtherRecords()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var item = await f.RecordAsync(new TextProfile(new string('文', 12000)), Token);
        var other = await f.RecordAsync(new TextProfile("valid"), Token);
        var backup = await f.ExportAsync([item, other], Token);
        using (var zip = ZipFile.Open(backup, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("history.json")!;
            HistoryExportDocument document;
            using (var input = entry.Open())
                document = (await JsonSerializer.DeserializeAsync<HistoryExportDocument>(input, HistoryExporter.JsonOptions, Token))!;
            document = document with
            {
                Records = document.Records.Select(record => record.Hash == item.Hash ? record with { Text = "unrelated preview" } : record).ToArray()
            };
            entry.Delete();
            await using var output = zip.CreateEntry("history.json").Open();
            await JsonSerializer.SerializeAsync(output, document, HistoryExporter.JsonOptions, Token);
        }
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.Contains("preview", await File.ReadAllTextAsync(result.ReportPath!, Token));
        Assert.AreEqual(other.Hash, (await f.Db.HistoryRecords.SingleAsync(Token)).Hash);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InsufficientDiskSpace_StopsBeforeCopyOrExtraction(bool group)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var file = Path.Combine(f.Root, "large.bin");
        await File.WriteAllBytesAsync(file, new byte[1024 * 1024], Token);
        var first = await f.RecordAsync(new TextProfile("already committed"), Token);
        var attachment = await f.RecordAsync(group ? new GroupProfile([file]) : new FileProfile(file), Token);
        var later = await f.RecordAsync(new TextProfile("not processed"), Token);
        var backup = await f.ExportAsync([first with { Timestamp = attachment.Timestamp.AddDays(1) },
            attachment, later with { Timestamp = attachment.Timestamp.AddDays(-1) }], Token);
        var checks = 0;
        var importer = new HistoryImporter(f, f.Manager, f.Config, _ => ++checks == 1 && group ? long.MaxValue : 0);
        using var plan = await HistoryImporter.PrepareAsync(backup, Token);
        var result = await importer.ImportAsync(plan, null, Token);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.IsNotNull(result.Error);
        Assert.Contains("disk space", result.Error);
        Assert.AreEqual(1, result.FailureCount);
        Assert.AreEqual(first.Hash, (await f.Db.HistoryRecords.SingleAsync(Token)).Hash);
        Assert.AreEqual(group ? 2 : 1, checks);
        AssertNoImportDirectories(Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), attachment.ProfileType, attachment.Hash));
    }

    private static void AssertNoImportDirectories(string root)
    {
        if (Directory.Exists(root))
            Assert.IsEmpty(Directory.GetDirectories(root, "import-*"));
    }

    private sealed class InlineProgress(Action<int> callback) : IProgress<int>
    {
        public void Report(int value) => callback(value);
    }
}
