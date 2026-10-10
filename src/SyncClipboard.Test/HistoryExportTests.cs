using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Profiles.Models;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryExportTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    private sealed class Fixture : IProfileEnv, IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("SyncClipboard-Export-");
        public string Output { get; }
        public HistoryExporter Exporter { get; }
        public Fixture()
        {
            Output = Directory.CreateDirectory(Path.Combine(_root.FullName, "output")).FullName;
            Directory.CreateDirectory(GetHistoryPersistentDir());
            Exporter = new(this);
        }
        public string GetPersistentDir() => _root.FullName;
        public string GetHistoryPersistentDir() => Path.Combine(_root.FullName, "history");
        public void Dispose() => _root.Delete(true);
        public async Task<HistoryExportItem> SaveAsync(Profile profile, CancellationToken token)
        {
            var content = await profile.Persist(GetHistoryPersistentDir(), token);
            var timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            return new(content, timestamp, timestamp.AddHours(1), timestamp.AddHours(2), true, true, "test device");
        }
        public async Task<HistoryExportResult> ExportAsync(HistoryExportItem[] items, CancellationToken token,
            IProgress<HistoryExportProgress>? progress = null)
        {
            using var plan = await Exporter.EstimateAsync(items, null, token);
            return await Exporter.ExportAsync(plan, Output, progress, token);
        }
    }

    private static async Task<HistoryExportDocument> ReadDocumentAsync(Stream stream, CancellationToken token) =>
        (await JsonSerializer.DeserializeAsync<HistoryExportDocument>(stream, HistoryExporter.JsonOptions, token))!;

    [TestMethod]
    public async Task InlineText_ExportsJsonWithOriginalContentAndMetadata()
    {
        using var fixture = new Fixture();
        const string text = "  中文🙂\nline two\r\n\t ";
        var item = await fixture.SaveAsync(new TextProfile(text), Token);
        var result = await fixture.ExportAsync([item], Token);

        Assert.IsNull(result.Error);
        Assert.AreEqual(1, result.ExportedCount);
        Assert.AreEqual(".json", Path.GetExtension(result.ArchivePath));
        Assert.IsNull(result.ReportPath);
        await using var file = File.OpenRead(result.ArchivePath!);
        var document = await ReadDocumentAsync(file, Token);
        Assert.AreEqual("syncclipboard-history", document.Format);
        Assert.AreEqual(1, document.FormatVersion);
        var record = document.Records.Single();
        Assert.AreEqual(text, record.Text);
        Assert.AreEqual(item.Content.Hash.ToUpperInvariant(), record.Hash);
        Assert.AreEqual(item.Timestamp, record.Timestamp);
        Assert.AreEqual(item.LastModified, record.LastModified);
        Assert.AreEqual(item.LastAccessed, record.LastAccessed);
        Assert.IsTrue(record.Starred && record.Pinned);
        Assert.AreEqual(item.From, record.From);
        Assert.IsFalse(record.HasTransferData);
        Assert.IsNull(record.TransferData);
    }

    [TestMethod]
    public async Task MixedTypes_PreserveTransferBytesAndProfileDirectories()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.GetPersistentDir(), "same-name.txt");
        await File.WriteAllTextAsync(path, "file contents", Token);
        Profile[] profiles = [new TextProfile("short"), new TextProfile(new string('文', 12000)),
            new FileProfile(path), new ImageProfile(path), new GroupProfile([path])];
        var items = new List<HistoryExportItem>();
        var expected = new Dictionary<string, byte[]>();
        foreach (var profile in profiles)
        {
            // Prepare the real transfer representation before taking the persistent snapshot.
            await profile.Persist(fixture.GetHistoryPersistentDir(), Token);
            var transfer = await profile.PrepareTransferData(fixture.GetHistoryPersistentDir(), Token);
            var item = await fixture.SaveAsync(profile, Token);
            items.Add(item);
            if (transfer is not null)
                expected[item.ProfileId] = await File.ReadAllBytesAsync(transfer.Path, Token);
        }
        var originalFiles = Directory.GetFiles(fixture.GetHistoryPersistentDir(), "*", SearchOption.AllDirectories);
        var result = await fixture.ExportAsync([.. items], Token);
        Assert.IsNull(result.Error);
        Assert.AreEqual(5, result.ExportedCount);
        Assert.AreEqual(".zip", Path.GetExtension(result.ArchivePath));
        using var zip = ZipFile.OpenRead(result.ArchivePath!);
        await using var manifest = zip.GetEntry("history.json")!.Open();
        var document = await ReadDocumentAsync(manifest, Token);
        Assert.HasCount(5, document.Records);
        Assert.HasCount(5, zip.Entries);
        foreach (var record in document.Records.Where(record => record.HasTransferData))
        {
            var data = record.TransferData!;
            var id = $"{record.Type}-{record.Hash}";
            Assert.StartsWith($"files/{id}/", data.Path);
            await using var source = zip.GetEntry(data.Path)!.Open();
            using var bytes = new MemoryStream();
            await source.CopyToAsync(bytes, Token);
            CollectionAssert.AreEqual(expected[id], bytes.ToArray());
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(expected[id])), data.Sha256);
            Assert.AreEqual((long)expected[id].Length, data.Size);
        }
        CollectionAssert.AreEquivalent(originalFiles,
            Directory.GetFiles(fixture.GetHistoryPersistentDir(), "*", SearchOption.AllDirectories));
        Assert.AreEqual("file contents", await File.ReadAllTextAsync(path, Token));
    }

    [TestMethod]
    public async Task MissingLongText_IsSkippedWithReportAndRemainingInlineTextUsesJson()
    {
        using var fixture = new Fixture();
        var missing = await fixture.SaveAsync(new TextProfile(new string('T', 12000)), Token);
        var source = Profile.GetFullPath(fixture.GetHistoryPersistentDir(), missing.Content.Type,
            missing.Content.Hash, missing.Content.TransferDataFile)!;
        File.Delete(source);
        var inline = await fixture.SaveAsync(new TextProfile("keep"), Token);
        var result = await fixture.ExportAsync([missing, inline], Token);
        Assert.AreEqual(1, result.ExportedCount);
        Assert.AreEqual(1, result.MissingCount);
        Assert.AreEqual(0, result.OtherCount);
        Assert.AreEqual(".json", Path.GetExtension(result.ArchivePath));
        Assert.AreEqual(fixture.Output, Path.GetDirectoryName(result.ReportPath));
        var report = await File.ReadAllTextAsync(result.ReportPath!, Token);
        Assert.Contains(missing.ProfileId, report);
        Assert.Contains(source, report);
        Assert.Contains("MissingFile", report);
        await using var file = File.OpenRead(result.ArchivePath!);
        Assert.AreEqual("keep", (await ReadDocumentAsync(file, Token)).Records.Single().Text);
    }

    [TestMethod]
    public async Task AllMissing_ProducesOnlyReportInSelectedDirectory()
    {
        using var fixture = new Fixture();
        var item = await fixture.SaveAsync(new TextProfile(new string('T', 12000)), Token);
        File.Delete(Profile.GetFullPath(fixture.GetHistoryPersistentDir(), item.Content.Type,
            item.Content.Hash, item.Content.TransferDataFile)!);
        var result = await fixture.ExportAsync([item], Token);
        Assert.IsNull(result.ArchivePath);
        Assert.IsNull(result.Error);
        Assert.AreEqual(0, result.ExportedCount);
        Assert.AreEqual(1, result.MissingCount);
        CollectionAssert.AreEqual(new[] { result.ReportPath }, Directory.GetFiles(fixture.Output));
    }

    [TestMethod]
    public async Task CorruptAttachment_IsOtherFailureAndDoesNotEnterArchive()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.GetPersistentDir(), "source.txt");
        await File.WriteAllTextAsync(path, "original", Token);
        var item = await fixture.SaveAsync(new FileProfile(path), Token);
        await File.WriteAllTextAsync(path, "modified", Token);
        var result = await fixture.ExportAsync([item], Token);
        Assert.IsNull(result.ArchivePath);
        Assert.AreEqual(0, result.MissingCount);
        Assert.AreEqual(HistoryExportFailure.InvalidData, result.Skipped.Single().Reason);
        Assert.IsNotNull(result.ReportPath);
    }

    [TestMethod]
    public async Task GroupWithoutTransferFile_GeneratesTransferInPersistentArea()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.GetPersistentDir(), "source.txt");
        await File.WriteAllTextAsync(path, "original", Token);
        var item = await fixture.SaveAsync(new GroupProfile([path]), Token);
        var result = await fixture.ExportAsync([item], Token);
        Assert.IsNull(result.Error);
        Assert.AreEqual(1, result.ExportedCount);
        using var zip = ZipFile.OpenRead(result.ArchivePath!);
        await using var manifest = zip.GetEntry("history.json")!.Open();
        var record = (await ReadDocumentAsync(manifest, Token)).Records.Single();
        await using var nestedStream = zip.GetEntry(record.TransferData!.Path)!.Open();
        using var nested = new ZipArchive(nestedStream, ZipArchiveMode.Read);
        using var reader = new StreamReader(nested.Entries.Single().Open());
        Assert.AreEqual("original", await reader.ReadToEndAsync(Token));
        Assert.AreEqual("original", await File.ReadAllTextAsync(path, Token));
    }

    [TestMethod]
    public async Task GroupWithOnlyTransferArchive_RemainsExportableWithoutOriginalFiles()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.GetPersistentDir(), "source.txt");
        await File.WriteAllTextAsync(path, "original", Token);
        var profile = new GroupProfile([path]);
        var transfer = await profile.PrepareTransferData(fixture.GetHistoryPersistentDir(), Token);
        var item = await fixture.SaveAsync(profile, Token);
        var expected = await File.ReadAllBytesAsync(transfer!.Path, Token);
        File.Delete(path);
        var result = await fixture.ExportAsync([item], Token);
        Assert.IsNull(result.Error);
        Assert.AreEqual(1, result.ExportedCount);
        Assert.AreEqual(0, result.MissingCount);
        using var zip = ZipFile.OpenRead(result.ArchivePath!);
        var attachment = zip.Entries.Single(entry => entry.FullName.StartsWith("files/", StringComparison.Ordinal));
        await using var source = attachment.Open();
        using var bytes = new MemoryStream();
        await source.CopyToAsync(bytes, Token);
        CollectionAssert.AreEqual(expected, bytes.ToArray());
    }

    [TestMethod]
    public async Task EmptySelection_DoesNotCreateFiles()
    {
        using var fixture = new Fixture();
        var result = await fixture.ExportAsync([], Token);
        Assert.AreEqual(0, result.ExportedCount);
        Assert.IsNull(result.Error);
        Assert.IsNull(result.ArchivePath);
        Assert.IsNull(result.ReportPath);
        Assert.IsEmpty(Directory.GetFiles(fixture.Output));
    }

    [TestMethod]
    public async Task CancellationDuringCopy_RemovesUncommittedArchiveAndReportsZeroSuccess()
    {
        using var fixture = new Fixture();
        var item = await fixture.SaveAsync(new TextProfile(new string('T', 2_000_000)), Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        // Cancellation after the attachment has been written, before the archive is committed.
        var progress = new CallbackProgress(value =>
        {
            if (value.WrittenBytes > 0)
                cancellation.Cancel();
        });
        var result = await fixture.ExportAsync([item], cancellation.Token, progress);
        Assert.IsTrue(result.Canceled);
        Assert.IsNull(result.ArchivePath);
        Assert.AreEqual(0, result.ExportedCount);
        Assert.AreEqual(HistoryExportFailure.Canceled, result.Skipped.Single().Reason);
        CollectionAssert.AreEqual(new[] { result.ReportPath }, Directory.GetFiles(fixture.Output));
    }

    [TestMethod]
    public async Task UnavailableDestination_ReturnsArchiveAndReportFailures()
    {
        using var fixture = new Fixture();
        var item = await fixture.SaveAsync(new TextProfile("keep"), Token);
        Directory.Delete(fixture.Output);
        var result = await fixture.ExportAsync([item], Token);
        Assert.IsNull(result.ArchivePath);
        Assert.IsNull(result.ReportPath);
        Assert.IsNotNull(result.Error);
        Assert.IsNotNull(result.ReportError);
        Assert.AreEqual(0, result.ExportedCount);
        Assert.AreEqual(HistoryExportFailure.ArchiveFailed, result.Skipped.Single().Reason);
    }

    [TestMethod]
    public async Task UnknownTypeWithBoundTransferHash_ExportsThroughGenericTransferContract()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.GetPersistentDir(), "future.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3], Token);
        var known = await fixture.SaveAsync(new TextProfile("metadata"), Token);
        var item = known with
        {
            Content = known.Content with
            {
                Type = (ProfileType)99,
                TransferDataFile = path,
                TransferDataHash = Convert.ToHexString(SHA256.HashData([1, 2, 3]))
            }
        };
        var result = await fixture.ExportAsync([item], Token);
        Assert.IsNull(result.Error);
        Assert.AreEqual(1, result.ExportedCount);
        Assert.AreEqual(".zip", Path.GetExtension(result.ArchivePath));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnknownTypeInvalidAttachmentIsExcludedDuringEstimate(bool missing)
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.GetPersistentDir(), "future.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 4], Token);
        var known = await fixture.SaveAsync(new TextProfile("metadata"), Token);
        var item = known with
        {
            Content = known.Content with
            {
                Type = (ProfileType)99,
                TransferDataFile = path,
                TransferDataHash = Convert.ToHexString(SHA256.HashData([1, 2, 3]))
            }
        };
        if (missing)
            File.Delete(path);
        using var plan = await fixture.Exporter.EstimateAsync([item], null, Token);
        Assert.IsEmpty(plan.Items);
        Assert.AreEqual(0L, plan.EstimatedBytes);
        Assert.AreEqual(missing ? HistoryExportFailure.MissingFile : HistoryExportFailure.InvalidData,
            plan.Skipped.Single().Reason);
        var result = await fixture.Exporter.ExportAsync(plan, fixture.Output, null, Token);
        Assert.AreEqual(0, result.ExportedCount);
        Assert.IsNull(result.ArchivePath);
        Assert.Contains(item.ProfileId, await File.ReadAllTextAsync(result.ReportPath!, Token));
    }

    [TestMethod]
    public async Task CommitDoesNotOverwriteExistingArchive()
    {
        using var fixture = new Fixture();
        var existing = Path.Combine(fixture.Output, "history.json");
        var partial = Path.Combine(fixture.Output, ".pending");
        await File.WriteAllTextAsync(existing, "old", Token);
        await File.WriteAllTextAsync(partial, "new", Token);
        var committed = HistoryExporter.Commit(partial, fixture.Output, "history", ".json");
        Assert.AreNotEqual(existing, committed);
        Assert.AreEqual("old", await File.ReadAllTextAsync(existing, Token));
        Assert.AreEqual("new", await File.ReadAllTextAsync(committed, Token));
    }

    [TestMethod]
    public async Task EstimatePreparesAndReusesGroupTransferAndTracksRemovedSelections()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.GetPersistentDir(), "source.txt");
        await File.WriteAllTextAsync(path, "original", Token);
        var item = await fixture.SaveAsync(new GroupProfile([path]), Token);
        HistoryRecordKey[] selected = [new(item.Content.Type, item.Content.Hash), new(ProfileType.Text, new string('A', 64))];
        var before = Directory.GetFiles(fixture.GetHistoryPersistentDir(), "*", SearchOption.AllDirectories);
        using var plan = await fixture.Exporter.EstimateAsync([item], selected, Token);
        var prepared = plan.Items.Single().Content.TransferDataFile!;
        Assert.IsTrue(File.Exists(prepared));
        Assert.IsGreaterThan(new FileInfo(prepared).Length, plan.EstimatedBytes);
        var preparedBytes = await File.ReadAllBytesAsync(prepared, Token);
        Assert.AreEqual(HistoryExportFailure.RecordRemoved, plan.Skipped.Single().Reason);
        var result = await fixture.Exporter.ExportAsync(plan, fixture.Output, null, Token);
        Assert.AreEqual(1, result.ExportedCount);
        Assert.AreEqual(1, result.OtherCount);
        CollectionAssert.AreEquivalent(before, Directory.GetFiles(fixture.GetHistoryPersistentDir(), "*", SearchOption.AllDirectories));
        using var zip = ZipFile.OpenRead(result.ArchivePath!);
        var attachment = zip.Entries.Single(entry => entry.FullName.StartsWith("files/", StringComparison.Ordinal));
        await using var source = attachment.Open();
        using var bytes = new MemoryStream();
        await source.CopyToAsync(bytes, Token);
        CollectionAssert.AreEqual(preparedBytes, bytes.ToArray());
    }

    [TestMethod]
    public async Task EstimateIncludesLegacyLongTextAttachmentSize()
    {
        using var fixture = new Fixture();
        var saved = await fixture.SaveAsync(new TextProfile(new string('文', 12000)), Token);
        var legacy = saved with { Content = saved.Content with { TransferDataFile = null, TransferDataHash = null } };
        using var currentPlan = await fixture.Exporter.EstimateAsync([saved], null, Token);
        using var legacyPlan = await fixture.Exporter.EstimateAsync([legacy], null, Token);
        Assert.AreEqual(currentPlan.EstimatedBytes, legacyPlan.EstimatedBytes);
        Assert.IsGreaterThan(36000L, legacyPlan.EstimatedBytes);
    }

    [TestMethod]
    public async Task ExportRechecksPreparedFilesAfterEstimate()
    {
        using var fixture = new Fixture();
        var item = await fixture.SaveAsync(new TextProfile(new string('T', 12000)), Token);
        using var plan = await fixture.Exporter.EstimateAsync([item], null, Token);
        File.Delete(plan.Items.Single().Content.TransferDataFile!);
        var result = await fixture.Exporter.ExportAsync(plan, fixture.Output, null, Token);
        Assert.AreEqual(0, result.ExportedCount);
        Assert.AreEqual(1, result.MissingCount);
        Assert.IsNull(result.ArchivePath);
        Assert.IsNotNull(result.ReportPath);
    }

    [TestMethod]
    public async Task EstimateExcludesFailedRecordsFromCountAndSizeAndPreservesReportReasons()
    {
        using var fixture = new Fixture();
        var valid = await fixture.SaveAsync(new TextProfile("keep"), Token);
        var missing = await fixture.SaveAsync(new TextProfile(new string('T', 12000)), Token);
        var missingPath = Profile.GetFullPath(fixture.GetHistoryPersistentDir(), missing.Content.Type,
            missing.Content.Hash, missing.Content.TransferDataFile)!;
        var missingBytes = await File.ReadAllBytesAsync(missingPath, Token);
        File.Delete(missingPath);
        var path = Path.Combine(fixture.GetPersistentDir(), "source.txt");
        await File.WriteAllTextAsync(path, "original", Token);
        var corrupt = await fixture.SaveAsync(new FileProfile(path), Token);
        await File.WriteAllTextAsync(path, "modified", Token);
        using var baseline = await fixture.Exporter.EstimateAsync([valid], null, Token);
        using var plan = await fixture.Exporter.EstimateAsync([missing, valid, corrupt], null, Token);
        Assert.AreEqual(valid.ProfileId, plan.Items.Single().ProfileId);
        Assert.AreEqual(baseline.EstimatedBytes, plan.EstimatedBytes);
        Assert.AreEqual(HistoryExportFailure.MissingFile, plan.Skipped.Single(x => x.ProfileId == missing.ProfileId).Reason);
        Assert.AreEqual(HistoryExportFailure.InvalidData, plan.Skipped.Single(x => x.ProfileId == corrupt.ProfileId).Reason);

        // A record excluded during estimation stays excluded even if its source becomes available again.
        await File.WriteAllBytesAsync(missingPath, missingBytes, Token);
        var result = await fixture.Exporter.ExportAsync(plan, fixture.Output, null, Token);
        Assert.AreEqual(1, result.ExportedCount);
        Assert.AreEqual(1, result.MissingCount);
        Assert.AreEqual(1, result.OtherCount);
        Assert.AreEqual(".json", Path.GetExtension(result.ArchivePath));
        var report = await File.ReadAllTextAsync(result.ReportPath!, Token);
        Assert.Contains(missing.ProfileId, report);
        Assert.Contains(corrupt.ProfileId, report);
        Assert.Contains("MissingFile", report);
        Assert.Contains("InvalidData", report);
    }

    [TestMethod]
    public async Task RepeatedGroupEstimatesAndExportsDoNotAccumulateTransferFiles()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.GetPersistentDir(), "source.txt");
        await File.WriteAllTextAsync(path, "original", Token);
        var group = await fixture.SaveAsync(new GroupProfile([path]), Token);
        var existing = await fixture.SaveAsync(new TextProfile(new string('T', 12000)), Token);
        var before = Directory.GetFiles(fixture.GetHistoryPersistentDir(), "*", SearchOption.AllDirectories);
        for (var i = 0; i < 2; i++)
        {
            // Closing the estimate without exporting must release only newly created files.
            using (var plan = await fixture.Exporter.EstimateAsync([group, existing], null, Token))
            {
                Assert.AreEqual(before.Length + 1,
                    Directory.GetFiles(fixture.GetHistoryPersistentDir(), "*", SearchOption.AllDirectories).Length);
            }
            CollectionAssert.AreEquivalent(before,
                Directory.GetFiles(fixture.GetHistoryPersistentDir(), "*", SearchOption.AllDirectories));
            var result = await fixture.ExportAsync([group, existing], Token);
            Assert.IsNull(result.Error);
            Assert.AreEqual(2, result.ExportedCount);
            CollectionAssert.AreEquivalent(before,
                Directory.GetFiles(fixture.GetHistoryPersistentDir(), "*", SearchOption.AllDirectories));
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task InterruptedExportRemovesGeneratedTransferFiles(bool cancel)
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.GetPersistentDir(), "source.txt");
        await File.WriteAllTextAsync(path, "original", Token);
        var item = await fixture.SaveAsync(new GroupProfile([path]), Token);
        var before = Directory.GetFiles(fixture.GetHistoryPersistentDir(), "*", SearchOption.AllDirectories);
        using var plan = await fixture.Exporter.EstimateAsync([item], null, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (cancel)
            cancellation.Cancel();
        else
            Directory.Delete(fixture.Output);
        var result = await fixture.Exporter.ExportAsync(plan, fixture.Output, null, cancellation.Token);
        Assert.AreEqual(0, result.ExportedCount);
        Assert.IsNull(result.ArchivePath);
        Assert.IsTrue(result.Canceled || result.Error is not null);
        CollectionAssert.AreEquivalent(before,
            Directory.GetFiles(fixture.GetHistoryPersistentDir(), "*", SearchOption.AllDirectories));
    }

    [TestMethod]
    public async Task ChangedBytesDuringCopy_AreRejected()
    {
        using var source = new MemoryStream([1, 2, 3]);
        using var target = new MemoryStream();
        var expected = Convert.ToHexString(SHA256.HashData([1, 2, 4]));
        Exception? sourceFailure = null;
        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            HistoryExporter.CopyVerifiedAsync(source, target, expected, _ => { }, Token, ex => sourceFailure = ex));
        Assert.AreSame(error, sourceFailure);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CopyReportsSourceReadErrorsButNotDestinationWriteErrors(bool failRead)
    {
        using var source = failRead ? new FailingReadStream() : new MemoryStream([1, 2, 3]);
        using var target = failRead ? new MemoryStream() : new FailingWriteStream();
        var expected = Convert.ToHexString(SHA256.HashData([1, 2, 3]));
        Exception? sourceFailure = null;
        var error = await Assert.ThrowsExactlyAsync<IOException>(() =>
            HistoryExporter.CopyVerifiedAsync(source, target, expected, _ => { }, Token, ex => sourceFailure = ex));
        if (failRead)
            Assert.AreSame(error, sourceFailure);
        else
            Assert.IsNull(sourceFailure);
    }

    private sealed class FailingReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Source read failed."));
    }

    private sealed class FailingWriteStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("Destination write failed."));
    }

    private sealed class CallbackProgress(Action<HistoryExportProgress> callback) : IProgress<HistoryExportProgress>
    {
        public void Report(HistoryExportProgress value) => callback(value);
    }
}
