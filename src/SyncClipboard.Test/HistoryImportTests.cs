using Microsoft.EntityFrameworkCore;
using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.History.HistoryExport;
using SyncClipboard.Core.Utilities.History.HistoryImport;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SyncClipboard.Test;

[TestClass]
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
        public MemoryDb Db { get; } = new();
        public HistoryManager Manager { get; }
        public HistoryImporter Importer { get; }
        public string Root => _root.FullName;
        public string GetPersistentDir() => Path.Combine(Root, "source");
        public string GetHistoryPersistentDir() => Path.Combine(Root, "restored");

        public Fixture()
        {
            Manager = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
            SetField("_dbContext", Db);
            SetField("_dbSemaphore", _semaphore);
            SetField("_logger", Mock.Of<ILogger>());
            SetField("_profileEnv", this);
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

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _semaphore.Dispose();
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
        var result = await f.ImportAsync(backup, Token);
        Assert.IsEmpty(result.Failures);
        Assert.AreEqual(5, result.ImportedCount);
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
            Assert.AreEqual(original.LastModified, restored.LastModified.ToUniversalTime());
            Assert.AreEqual(original.LastAccessed, restored.LastAccessed.ToUniversalTime());
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
    public async Task InlineJson_DoesNotOverwriteExistingOrDeletedRecords()
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
        Assert.IsTrue(existing.IsDeleted);
        Assert.IsEmpty(result.Failures);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("hash")]
    [DataRow("size")]
    [DataRow("path")]
    [DataRow("name")]
    [DataRow("duplicate-entry")]
    public async Task InvalidAttachment_IsReportedAndOtherRecordsStillImport(string mode)
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
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(1, result.ImportedCount);
        Assert.HasCount(1, result.Failures);
        Assert.IsNotNull(result.ReportPath);
        Assert.Contains(item.Hash, await File.ReadAllTextAsync(result.ReportPath, Token));
        Assert.IsFalse(File.Exists(Path.Combine(f.GetHistoryPersistentDir(), "escape.txt")));
        var recordFolder = Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), item.ProfileType, item.Hash);
        Assert.IsEmpty(Directory.GetFileSystemEntries(recordFolder));
    }

    [TestMethod]
    public async Task Cancellation_KeepsAlreadyCommittedRecords()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var backup = await f.ExportAsync([await f.RecordAsync(new TextProfile("one"), Token), await f.RecordAsync(new TextProfile("two"), Token)], Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var result = await f.ImportAsync(backup, cancellation.Token, new InlineProgress(_ => cancellation.Cancel()));
        Assert.IsTrue(result.Canceled);
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
    public async Task InvalidInlineHash_IsNotPersisted()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var item = (await f.RecordAsync(new TextProfile("valid"), Token)) with { Hash = new string('0', 64) };
        var path = Path.Combine(f.Root, "invalid.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new HistoryExportDocument("syncclipboard-history", 1, "test", DateTime.UtcNow, [item]), HistoryExporter.JsonOptions), Token);
        var result = await f.ImportAsync(path, Token);
        Assert.HasCount(1, result.Failures);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
    }

    [TestMethod]
    public async Task MissingAttachment_IsRepairedWithoutChangingLocalProperties()
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
            Version = 42
        };
        f.Db.HistoryRecords.Add(local);
        await f.Db.SaveChangesAsync(Token);
        var snapshot = (local.Timestamp, local.LastModified, local.LastAccessed);
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(1, result.RepairedCount);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.IsEmpty(result.Failures);
        Assert.AreEqual(snapshot, (local.Timestamp, local.LastModified, local.LastAccessed));
        Assert.AreEqual("local text", local.Text);
        Assert.AreEqual("local device", local.From);
        Assert.IsFalse(local.Stared || local.Pinned);
        Assert.AreEqual(42, local.Version);
        Assert.AreEqual(HistorySyncStatus.Synced, local.SyncStatus);
        Assert.IsTrue(local.IsLocalFileReady);
        var restored = Profile.GetFullPath(f.GetHistoryPersistentDir(), local.Type, local.Hash, local.TransferDataFile)!;
        Assert.AreEqual("payload", await File.ReadAllTextAsync(restored, Token));
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
        Assert.HasCount(1, result.Failures);
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
        Assert.IsEmpty(Directory.GetFileSystemEntries(Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), item.ProfileType, item.Hash)));
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
        Assert.IsEmpty(result.Failures);
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
        Assert.HasCount(1, result.Failures);
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
    }

    private sealed class InlineProgress(Action<int> callback) : IProgress<int>
    {
        public void Report(int value) => callback(value);
    }
}
