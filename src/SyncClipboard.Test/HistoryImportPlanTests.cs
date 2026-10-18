using SyncClipboard.Core.Utilities.History.HistoryExport;
using SyncClipboard.Core.Utilities.History.HistoryImport;
using System.IO.Compression;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryImportPlanTests
{
    [TestMethod]
    public void IndexIncludesOnlyReferencedEntriesAndRejectsDuplicates()
    {
        using var buffer = new MemoryStream();
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("files/data");
        var duplicate = archive.CreateEntry("files/data");
        var unrelated = archive.CreateEntry("unrelated");
        var record = CreateRecord();
        var index = HistoryImportPlan.IndexEntries([unrelated, entry], [record], CancellationToken.None);
        Assert.HasCount(1, index);
        Assert.AreSame(entry, index["files/data"]);
        index = HistoryImportPlan.IndexEntries([entry, duplicate, unrelated], [record], CancellationToken.None);
        Assert.HasCount(1, index);
        Assert.IsNull(index["files/data"]);
    }

    [TestMethod]
    public void IndexCanBeCanceledWhileScanningUnrelatedEntries()
    {
        using var buffer = new MemoryStream();
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Create);
        var unrelated = archive.CreateEntry("unrelated");
        using var cancellation = new CancellationTokenSource();
        IEnumerable<ZipArchiveEntry> Entries()
        {
            yield return unrelated;
            cancellation.Cancel();
            yield return unrelated;
            Assert.Fail("Indexing must stop as soon as cancellation is observed.");
        }
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            HistoryImportPlan.IndexEntries(Entries(), [CreateRecord()], cancellation.Token));
    }

    private static HistoryExportRecord CreateRecord() => new(
        "File", new string('A', 64), "data", 0, DateTime.UnixEpoch, DateTime.UnixEpoch, DateTime.UnixEpoch,
        false, false, "", new("data", "files/data", 0, new string('B', 64)));
}
