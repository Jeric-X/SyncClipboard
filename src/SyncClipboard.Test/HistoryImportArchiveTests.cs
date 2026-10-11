using SyncClipboard.Core.Utilities.History.HistoryImport;
using System.IO.Compression;
using System.Text;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryImportArchiveTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(10)]
    [DataRow(65534, true)]
    [DataRow(66000)]
    public async Task FilteredDirectory_ReadsReferencedPayloadInZipAndZip64(int unrelatedCount, bool legacy = false)
    {
        using var original = CreateArchive(unrelatedCount);
        using var source = legacy ? WithoutZip64End(original) : new MemoryStream(original.ToArray());
        using (var archive = await HistoryImportArchive.OpenAsync(source,
            new HashSet<string>(StringComparer.Ordinal) { "files/needed.txt" }, TestContext.CancellationTokenSource.Token))
        {
            Assert.HasCount(1, archive.Entries);
            using var reader = new StreamReader(archive.Entries[0].Open());
            Assert.AreEqual("needed payload", await reader.ReadToEndAsync(TestContext.CancellationTokenSource.Token));
        }
        Assert.IsTrue(source.CanRead, "The view must not close the plan's source stream.");
    }

    [TestMethod]
    public async Task DuplicateReferencedEntries_RemainDetectableWithoutRetainingEveryDuplicate()
    {
        using var source = new MemoryStream();
        using (var zip = new ZipArchive(source, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < 1000; i++)
                zip.CreateEntry("files/duplicate");
        }
        using var filtered = await HistoryImportArchive.OpenAsync(source,
            new HashSet<string> { "files/duplicate" }, TestContext.CancellationTokenSource.Token);
        Assert.HasCount(2, filtered.Entries);
        Assert.IsTrue(filtered.Entries.All(entry => entry.FullName == "files/duplicate"));
    }

    [TestMethod]
    public async Task CentralDirectoryScan_ObservesCancellationBeforeReadingAllEntries()
    {
        using var original = CreateArchive(1000);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
        using var source = new CancelingStream(original.ToArray(), cancellation);
        await Assert.ThrowsAsync<OperationCanceledException>(() => HistoryImportArchive.OpenAsync(source,
            new HashSet<string> { "files/needed.txt" }, cancellation.Token));
        Assert.IsLessThan(100, source.ReadCount);
    }

    [TestMethod]
    public async Task InvalidDirectoryOffset_IsRejectedBeforeScanning()
    {
        using var original = CreateArchive(1);
        var bytes = original.ToArray();
        bytes.AsSpan(bytes.Length - 6, 4).Fill(0xff);
        using var source = new MemoryStream(bytes);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HistoryImportArchive.OpenAsync(source,
            new HashSet<string> { "files/needed.txt" }, TestContext.CancellationTokenSource.Token));
    }

    private static MemoryStream WithoutZip64End(MemoryStream original)
    {
        var bytes = original.ToArray();
        var zip64 = bytes.AsSpan().LastIndexOf(new byte[] { 0x50, 0x4b, 0x06, 0x06 });
        if (zip64 < 0)
            return new MemoryStream(bytes);
        var result = new byte[zip64 + 22];
        bytes.AsSpan(0, zip64).CopyTo(result);
        bytes.AsSpan(bytes.Length - 22).CopyTo(result.AsSpan(zip64));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(zip64 + 12),
            checked((uint)System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(zip64 + 40))));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(zip64 + 16),
            checked((uint)System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(zip64 + 48))));
        return new MemoryStream(result);
    }

    private static MemoryStream CreateArchive(int unrelatedCount)
    {
        var source = new MemoryStream();
        using (var zip = new ZipArchive(source, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < unrelatedCount; i++)
                zip.CreateEntry($"unrelated/{i}");
            using var output = zip.CreateEntry("files/needed.txt").Open();
            output.Write(Encoding.UTF8.GetBytes("needed payload"));
        }
        return source;
    }

    private sealed class CancelingStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public int ReadCount { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++ReadCount == 20)
                cancellation.Cancel();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
