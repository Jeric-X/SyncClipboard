using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core;
using SyncClipboard.Core.Exceptions;
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
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=:memory:");
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
            SetField("_historyConfig", new HistoryConfig { HistoryRetentionMinutes = 60 });
            SetField("_runtimeHistoryConfig", new RuntimeHistoryConfig());
            SetField("_historyManagerHelper", new HistoryManagerHelper<HistoryRecord, DateTime>(Manager));
            Config = new ConfigManager(Path.Combine(Root, "SyncClipboard.json"), _configurationServices.Upgrader);
            Importer = new(this, Manager);
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

        public ScopeGuard UseApplication()
        {
            var previous = AppCore.TryGetCurrent();
            var services = new ServiceCollection().AddSingleton<IProfileEnv>(this).AddSingleton(Config)
                .BuildServiceProvider();
            var app = (AppCore)RuntimeHelpers.GetUninitializedObject(typeof(AppCore));
            SetPrivateField(app, "<Services>k__BackingField", services);
            SetPrivateField(app, "<ConfigManager>k__BackingField", Config);
            var current = typeof(AppCore).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
            current.SetValue(null, app);
            return new ScopeGuard(() =>
            {
                current.SetValue(null, previous);
                services.Dispose();
            });
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
        {
            var record = await f.RecordAsync(profile, Token);
            records.Add(record with { Hash = record.Hash.ToLowerInvariant() });
        }
        var backup = await f.ExportAsync([.. records], Token);
        using (var zip = ZipFile.Open(backup, ZipArchiveMode.Update))
        {
            var manifest = zip.GetEntry("history.json")!;
            HistoryExportDocument document;
            using (var input = manifest.Open())
                document = (await JsonSerializer.DeserializeAsync<HistoryExportDocument>(input, HistoryExporter.JsonOptions, Token))!;
            document = document with { Records = document.Records.Select(record => record with { Size = record.Size + 1 }).ToArray() };
            manifest.Delete();
            await using var output = zip.CreateEntry("history.json").Open();
            await JsonSerializer.SerializeAsync(output, document, HistoryExporter.JsonOptions, Token);
        }
        var syncNotifications = new List<HistoryRecord>();
        f.Manager.HistoryAdded += record => syncNotifications.Add(record);
        f.Manager.HistoryUpdated += record => syncNotifications.Add(record);
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(0, result.FailureCount);
        Assert.AreEqual(5, result.ImportedCount);
        Assert.HasCount(5, syncNotifications);
        Assert.IsNull(result.ReportPath);
        File.Delete(file);
        Directory.Delete(folder, true);
        Directory.Delete(f.GetPersistentDir(), true);
        foreach (var original in records)
        {
            var restored = await f.Db.HistoryRecords.SingleAsync(x => x.Type == original.ProfileType && x.Hash == original.Hash, Token);
            Assert.AreEqual(original.Hash.ToUpperInvariant(), restored.Hash);
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
        f.Manager.HistoryUpdated += record =>
        {
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
        f.Manager.HistoryAdded += _ => throw new InvalidOperationException("observer failure");
        f.Manager.HistoryAdded += record =>
        {
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
    [DataRow(false, 0)]
    [DataRow(true, 0)]
    [DataRow(false, 404)]
    [DataRow(false, 409)]
    public async Task OrdinaryPropertySync_ReconcilesDeletionAndHandlesServerResponse(bool deleted, int response)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        using var application = f.UseApplication();
        var record = new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = await Utility.CalculateSHA256("local text", Token),
            IsLocalFileReady = true,
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
                if (response == 404)
                    throw new RemoteHistoryNotFoundException("The deleted attachment must be uploaded again.");
                if (response == 409)
                    throw new RemoteHistoryConflictException("Remote is newer.", new HistoryRecordUpdateDto
                    {
                        IsDelete = true,
                        Version = 20,
                        LastModified = DateTimeOffset.UtcNow
                    });
                if (dto.IsDelete.HasValue)
                    remoteDeleted = dto.IsDelete.Value;
            }).Returns(Task.CompletedTask);
        var factory = (RemoteClipboardServerFactory)RuntimeHelpers.GetUninitializedObject(typeof(RemoteClipboardServerFactory));
        SetPrivateField(factory, "_current", remote.Object);
        SetPrivateField(factory, "_serverStateLock", new Lock());
        var runtime = new ConfigBase(Path.Combine(f.Root, "runtime.json"));
        using var queue = new HistoryTransferQueue(Mock.Of<ILogger>(), factory, f.Manager, f, f.Config, runtime);
        var releaseUpload = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Setup(x => x.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (_, ct) =>
            {
                await releaseUpload.Task.WaitAsync(ct);
                return null;
            });
        server.Setup(x => x.UploadHistoryAsync(It.IsAny<HistoryRecordDto>(), It.IsAny<FileHashInfo?>(),
                It.IsAny<IProgress<HttpDownloadProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoryRecordDto dto, FileHashInfo? data, IProgress<HttpDownloadProgress> progress, CancellationToken ct) =>
            {
                Assert.IsFalse(dto.IsDeleted);
                dto.Version = 21;
                return dto;
            });
        var syncer = (HistorySyncer)RuntimeHelpers.GetUninitializedObject(typeof(HistorySyncer));
        SetPrivateField(syncer, "_historyTransferQueue", queue);
        SetPrivateField(syncer, "_historyManager", f.Manager);
        SetPrivateField(syncer, "_remoteServerFactory", factory);
        SetPrivateField(syncer, "_logger", Mock.Of<ILogger>());

        await syncer.SyncOneAsync(record, Token);

        Assert.IsNotNull(sent);
        Assert.IsTrue(sent.Starred);
        Assert.IsTrue(sent.Pinned);
        Assert.AreEqual<bool?>(deleted, sent.IsDelete);
        Assert.AreEqual(response != 0 || deleted, remoteDeleted);
        f.Db.ChangeTracker.Clear();
        var saved = await f.Db.HistoryRecords.SingleAsync(Token);
        Assert.AreEqual(response == 404 ? HistorySyncStatus.LocalOnly : HistorySyncStatus.Synced, saved.SyncStatus);
        Assert.AreEqual(response == 409 || deleted, saved.IsDeleted);
        Assert.AreEqual(response == 409 ? 20 : 8, saved.Version);
        if (response == 404)
        {
            releaseUpload.TrySetResult(true);
            await queue.WaitAllTasks(Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
            f.Db.ChangeTracker.Clear();
            var uploaded = await f.Db.HistoryRecords.SingleAsync(Token);
            Assert.AreEqual(HistorySyncStatus.Synced, uploaded.SyncStatus);
            Assert.AreEqual(21, uploaded.Version);
            Assert.IsFalse(uploaded.IsDeleted);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ImportedRecord_AfterReloadingDatabaseRestoresRemoteDeletionThroughPropertySync(bool localDeleted)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var item = await f.RecordAsync(new TextProfile("restart import"), Token);
        var backup = await f.ExportAsync([item], Token);
        var oldTime = DateTime.UtcNow.AddHours(-1);
        f.Db.HistoryRecords.Add(new HistoryRecord
        {
            Type = item.ProfileType,
            Hash = item.Hash,
            Text = item.Text,
            IsDeleted = localDeleted,
            Version = 7,
            LastModified = oldTime,
            SyncStatus = HistorySyncStatus.Synced
        });
        await f.Db.SaveChangesAsync(Token);
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(0, result.FailureCount);
        // Resume using only the persisted record, without a live event subscriber.
        f.Db.ChangeTracker.Clear();
        var remoteRecord = new HistoryRecordDto
        {
            Type = item.ProfileType,
            Hash = item.Hash,
            Text = item.Text,
            IsDeleted = true,
            Version = 7,
            LastModified = oldTime
        };
        await f.Manager.SyncRemoteHistoryAsync([remoteRecord], Token);
        var saved = await f.Db.HistoryRecords.SingleAsync(Token);
        Assert.AreEqual(HistorySyncStatus.NeedSync, saved.SyncStatus);
        var remote = new Mock<IRemoteClipboardServer>();
        var server = remote.As<IOfficialSyncServer>();
        server.Setup(x => x.UpdateHistoryAsync(item.ProfileType, item.Hash, It.IsAny<HistoryRecordUpdateDto>(), It.IsAny<CancellationToken>()))
            .Callback<ProfileType, string, HistoryRecordUpdateDto, CancellationToken>((_, _, dto, _) =>
            {
                Assert.IsTrue(dto.LastModified > remoteRecord.LastModified);
                Assert.AreEqual(8, dto.Version);
                if (dto.IsDelete.HasValue)
                    remoteRecord.IsDeleted = dto.IsDelete.Value;
            }).Returns(Task.CompletedTask);
        var factory = (RemoteClipboardServerFactory)RuntimeHelpers.GetUninitializedObject(typeof(RemoteClipboardServerFactory));
        SetPrivateField(factory, "_current", remote.Object);
        SetPrivateField(factory, "_serverStateLock", new Lock());
        var syncer = (HistorySyncer)RuntimeHelpers.GetUninitializedObject(typeof(HistorySyncer));
        SetPrivateField(syncer, "_historyManager", f.Manager);
        SetPrivateField(syncer, "_remoteServerFactory", factory);
        SetPrivateField(syncer, "_logger", Mock.Of<ILogger>());
        await syncer.SyncNeedUpdateAsync(Token);
        Assert.IsFalse(remoteRecord.IsDeleted);
        f.Db.ChangeTracker.Clear();
        Assert.AreEqual(HistorySyncStatus.Synced, (await f.Db.HistoryRecords.SingleAsync(Token)).SyncStatus);
    }

    private static void SetPrivateField<T>(object target, string name, T value)
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
        var importer = existingRoot == "concurrent" ? new HistoryImporter(f, f.Manager, _ =>
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
        var first = await f.RecordAsync(new TextProfile("one"), Token);
        var later = await f.RecordAsync(new TextProfile("two"), Token);
        var document = new HistoryExportDocument("syncclipboard-history", 1, "test", DateTime.UtcNow, [first, null!, later]);
        var backup = Path.Combine(f.Root, "backup.json");
        await File.WriteAllTextAsync(backup, JsonSerializer.Serialize(document, HistoryExporter.JsonOptions), Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var result = await f.ImportAsync(backup, cancellation.Token, new InlineProgress(count =>
        {
            if (count == 2)
                cancellation.Cancel();
        }));
        Assert.IsTrue(result.Canceled);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.AreEqual(1, result.FailureCount);
        Assert.HasCount(1, await f.Db.HistoryRecords.ToListAsync(Token));
        Assert.IsNotNull(result.ReportPath);
        Assert.IsNull(result.ReportError);
        Assert.Contains("2. -: Unsupported record type.", await File.ReadAllTextAsync(result.ReportPath, Token));
        using var canceledPlan = await HistoryImporter.PrepareAsync(backup, Token);
        await f.DbSemaphore.WaitAsync(Token);
        try
        {
            var canceledResult = await f.Importer.ImportAsync(canceledPlan, null, cancellation.Token);
            Assert.IsTrue(canceledResult.Canceled);
        }
        finally
        {
            f.DbSemaphore.Release();
        }
        var saved = await f.Db.HistoryRecords.SingleAsync(Token);
        saved.Stared = saved.Pinned = false;
        saved.LastModified = saved.LastAccessed = DateTime.UtcNow.AddDays(-40);
        await f.Db.SaveChangesAsync(Token);
        await f.Manager.CleanupExpiredHistory(Token);
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
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
    [DataRow("timestamp", "malformed")]
    [DataRow("type", "numeric")]
    [DataRow("timestamp", "malformed", true)]
    [DataRow("type", "numeric", true)]
    public async Task MissingOrInvalidTimestamp_IsReportedAndOtherRecordsStillImport(string field, string mode, bool zip = false)
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
        else if (mode == "malformed")
            record[field] = "not-a-date";
        else if (mode == "numeric")
            record[field] = 42;
        else
            record[field] = JsonValue.Create(mode == "min" ? DateTime.MinValue : DateTime.MaxValue);
        var path = Path.Combine(f.Root, zip ? "invalid.zip" : "invalid.json");
        if (zip)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            await using var output = archive.CreateEntry("history.json").Open();
            await JsonSerializer.SerializeAsync(output, json, cancellationToken: Token);
        }
        else
        {
            await File.WriteAllTextAsync(path, json.ToJsonString(), Token);
        }
        var result = await f.ImportAsync(path, Token);
        Assert.AreEqual(1, result.FailureCount);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.IsNotNull(result.ReportPath);
        Assert.Contains(mode == "numeric" ? "type" : "timestamp", await File.ReadAllTextAsync(result.ReportPath, Token));
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
            File.OpenRead(backup), null, new HistoryImportDocument(document.Format, document.FormatVersion,
                document.AppVersion, document.ExportedAt, JsonSerializer.SerializeToElement(document.Records, HistoryExporter.JsonOptions)), Token);
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
            LastModified = DateTime.UtcNow.AddDays(-40),
            LastAccessed = DateTime.UtcNow.AddDays(-40),
            SyncStatus = deleted ? HistorySyncStatus.Synced : HistorySyncStatus.LocalOnly,
            Version = 42,
            IsDeleted = deleted
        };
        SetPrivateField(f.Manager, "_runtimeHistoryConfig", new RuntimeHistoryConfig { EnableSyncHistory = deleted });
        f.Db.HistoryRecords.Add(local);
        await f.Db.SaveChangesAsync(Token);
        var (Timestamp, LastModified, LastAccessed) = (local.Timestamp, local.LastModified, local.LastAccessed);
        var syncNotifications = new List<HistoryRecord>();
        f.Manager.HistoryUpdated += record => syncNotifications.Add(record);
        string? restoreDirectory = null;
        var importer = new HistoryImporter(f, f.Manager, directory =>
        {
            restoreDirectory = directory;
            f.Manager.CleanupExpiredHistory(Token).GetAwaiter().GetResult();
            return long.MaxValue;
        });
        using var plan = await HistoryImporter.PrepareAsync(backup, Token);
        var result = await importer.ImportAsync(plan, null, Token);
        Assert.AreEqual(Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), local.Type, local.Hash),
            Path.GetDirectoryName(restoreDirectory));
        Assert.AreEqual(1, result.RepairedCount);
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
        Assert.AreEqual(deleted ? HistorySyncStatus.NeedSync : HistorySyncStatus.LocalOnly, local.SyncStatus);
        Assert.IsTrue(local.IsLocalFileReady);
        var restored = Profile.GetFullPath(f.GetHistoryPersistentDir(), local.Type, local.Hash, local.TransferDataFile)!;
        Assert.AreEqual("payload", await File.ReadAllTextAsync(restored, Token));
        Assert.AreEqual(item.Hash.ToLowerInvariant(), local.Hash);
        SetPrivateField(f.Manager, "_runtimeHistoryConfig", new RuntimeHistoryConfig());
        local.SyncStatus = HistorySyncStatus.LocalOnly;
        local.LastModified = local.LastAccessed = DateTime.UtcNow.AddDays(-40);
        await f.Db.SaveChangesAsync(Token);
        await f.Manager.CleanupExpiredHistory(Token);
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
        f.Manager.HistoryUpdated += _ => notifications++;
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
        f.Manager.HistoryAdded += _ => throw new InvalidOperationException("observer failure");
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
    [DataRow(false)]
    [DataRow(true)]
    public async Task InsufficientDiskSpace_StopsBeforeAttachmentCopy(bool group)
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
        using var plan = await HistoryImporter.PrepareAsync(backup, Token);
        var transferSize = plan.Records.Single(record => record!.Hash == attachment.Hash)!.TransferData!.Size;
        var checks = 0;
        var importer = new HistoryImporter(f, f.Manager, _ =>
        {
            checks++;
            return group ? transferSize : 0;
        });
        var result = await importer.ImportAsync(plan, null, Token);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.IsNotNull(result.Error);
        Assert.Contains("disk space", result.Error);
        Assert.AreEqual(1, result.FailureCount);
        Assert.AreEqual(first.Hash, (await f.Db.HistoryRecords.SingleAsync(Token)).Hash);
        Assert.AreEqual(1, checks);
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
