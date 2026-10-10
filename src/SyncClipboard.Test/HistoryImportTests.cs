using Microsoft.EntityFrameworkCore;
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
        private readonly ConfigurationTestServices _configurationServices = new();
        public ConfigManager Config { get; }
        public MemoryDb Db { get; } = new();
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
        var uiNotifications = new List<HistoryRecord>();
        var syncNotifications = new List<HistoryRecord>();
        f.Manager.HistoryImported += uiNotifications.Add;
        f.Manager.HistoryAdded += syncNotifications.Add;
        f.Manager.HistoryUpdated += syncNotifications.Add;
        var result = await f.ImportAsync(backup, Token);
        Assert.IsEmpty(result.Failures);
        Assert.AreEqual(5, result.ImportedCount);
        Assert.HasCount(5, uiNotifications);
        Assert.IsEmpty(syncNotifications);
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
        var uiNotifications = new List<HistoryRecord>();
        var syncNotifications = new List<HistoryRecord>();
        f.Manager.HistoryImported += uiNotifications.Add;
        f.Manager.HistoryUpdated += syncNotifications.Add;
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(1, result.RepairedCount);
        Assert.AreEqual(local, uiNotifications.Single());
        Assert.IsEmpty(syncNotifications);
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
        Assert.AreEqual(item.Hash.ToLowerInvariant(), local.Hash);
        await f.Manager.RemoveHistory(local, Token);
        Assert.IsFalse(File.Exists(restored));
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
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
        f.Manager.HistoryImported += _ => throw new InvalidOperationException("observer failure");
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

    [TestMethod]
    [DataRow("Text")]
    [DataRow("LongText")]
    [DataRow("File")]
    [DataRow("Image")]
    [DataRow("Group")]
    public async Task IncorrectRecordSize_IsNotPersisted(string type)
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var file = Path.Combine(f.Root, "data.txt");
        await File.WriteAllTextAsync(file, "payload", Token);
        Profile profile = type switch
        {
            "Text" => new TextProfile("hello"),
            "LongText" => new TextProfile(new string('文', 12000)),
            "File" => new FileProfile(file),
            "Image" => new ImageProfile(file),
            _ => new GroupProfile([file])
        };
        var backup = await f.ExportAsync([await f.RecordAsync(profile, Token)], Token);
        await ChangeRecordSizeAsync(backup, size => size + 1, Token);
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.HasCount(1, result.Failures);
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
    }

    [TestMethod]
    public async Task CompressedGroup_RejectsExtractionBeyondDeclaredSize()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var file = Path.Combine(f.Root, "zeros.bin");
        await File.WriteAllBytesAsync(file, new byte[1024 * 1024], Token);
        var item = await f.RecordAsync(new GroupProfile([file]), Token);
        var backup = await f.ExportAsync([item], Token);
        await ChangeRecordSizeAsync(backup, _ => 1, Token);
        var result = await f.ImportAsync(backup, Token);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.Contains("extraction budget", result.Failures.Single().Reason);
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
        Assert.IsEmpty(Directory.GetDirectories(Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), item.ProfileType, item.Hash), "import-*"));
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
            Assert.IsEmpty(result.Failures);
            var restored = await f.Db.HistoryRecords.SingleAsync(record => record.Hash == group.Hash, Token);
            Assert.HasCount(2, restored.FilePath);
            foreach (var path in restored.FilePath)
                Assert.IsTrue(File.Exists(Profile.GetFullPath(f.GetHistoryPersistentDir(), restored.Type, restored.Hash, path)));
        }
        else
        {
            Assert.Contains("file and directory limit", result.Failures.Single().Reason);
            Assert.IsNotNull(result.ReportPath);
            Assert.Contains("file and directory limit", await File.ReadAllTextAsync(result.ReportPath, Token));
            Assert.IsEmpty(Directory.GetDirectories(Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), group.ProfileType, group.Hash), "import-*"));
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
        Assert.HasCount(1, result.Failures);
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
        Assert.Contains("display text", result.Failures.Single().Reason);
        Assert.AreEqual(other.Hash, (await f.Db.HistoryRecords.SingleAsync(Token)).Hash);
        Assert.IsEmpty(Directory.GetDirectories(Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), item.ProfileType, item.Hash), "import-*"));
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
        Assert.IsEmpty(result.Failures);
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
        Assert.Contains("Decoded text", result.Failures.Single().Reason);
        Assert.AreEqual(other.Hash, (await f.Db.HistoryRecords.SingleAsync(Token)).Hash);
        Assert.IsEmpty(Directory.GetDirectories(Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), ProfileType.Text, hash), "import-*"));
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
        Assert.Contains("preview", result.Failures.Single().Reason);
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
        Assert.HasCount(1, result.Failures);
        Assert.AreEqual(first.Hash, (await f.Db.HistoryRecords.SingleAsync(Token)).Hash);
        Assert.AreEqual(group ? 2 : 1, checks);
        Assert.IsEmpty(Directory.GetDirectories(Profile.QueryGetWorkingDir(f.GetHistoryPersistentDir(), attachment.ProfileType, attachment.Hash), "import-*"));
    }

    private static async Task ChangeRecordSizeAsync(string path, Func<long, long> change, CancellationToken token)
    {
        if (Path.GetExtension(path) == ".json")
        {
            var document = JsonSerializer.Deserialize<HistoryExportDocument>(await File.ReadAllTextAsync(path, token), HistoryExporter.JsonOptions)!;
            document = document with { Records = document.Records.Select(item => item with { Size = change(item.Size) }).ToArray() };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, HistoryExporter.JsonOptions), token);
            return;
        }
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var manifest = archive.GetEntry("history.json")!;
        HistoryExportDocument content;
        using (var input = manifest.Open())
            content = (await JsonSerializer.DeserializeAsync<HistoryExportDocument>(input, HistoryExporter.JsonOptions, token))!;
        content = content with { Records = content.Records.Select(item => item with { Size = change(item.Size) }).ToArray() };
        manifest.Delete();
        await using var output = archive.CreateEntry("history.json").Open();
        await JsonSerializer.SerializeAsync(output, content, HistoryExporter.JsonOptions, token);
    }

    private sealed class InlineProgress(Action<int> callback) : IProgress<int>
    {
        public void Report(int value) => callback(value);
    }
}
