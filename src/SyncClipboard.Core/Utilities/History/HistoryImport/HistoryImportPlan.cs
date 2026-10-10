using SyncClipboard.Core.Utilities.History.HistoryExport;
using System.IO.Compression;

namespace SyncClipboard.Core.Utilities.History.HistoryImport;

public sealed class HistoryImportPlan(string path, FileStream source, ZipArchive? archive, HistoryExportDocument document,
    CancellationToken token) : IDisposable
{
    public string Path { get; } = path;
    public IReadOnlyList<HistoryExportRecord> Records { get; } = document.Records;
    private readonly Dictionary<string, ZipArchiveEntry?> entries = IndexEntries(archive?.Entries ?? [], document.Records, token);

    internal static Dictionary<string, ZipArchiveEntry?> IndexEntries(
        IEnumerable<ZipArchiveEntry> archiveEntries, IReadOnlyList<HistoryExportRecord> records, CancellationToken token)
    {
        var referencedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            token.ThrowIfCancellationRequested();
            if (record.TransferData is { } transfer)
                referencedPaths.Add(transfer.Path);
        }

        var entries = new Dictionary<string, ZipArchiveEntry?>(StringComparer.Ordinal);
        foreach (var entry in archiveEntries)
        {
            token.ThrowIfCancellationRequested();
            if (referencedPaths.Contains(entry.FullName) && !entries.TryAdd(entry.FullName, entry))
                entries[entry.FullName] = null;
        }
        return entries;
    }

    internal ZipArchiveEntry GetTransferEntry(string path)
    {
        if (!entries.TryGetValue(path, out var entry))
            throw new FileNotFoundException("Transfer data is missing.", path);
        return entry ?? throw new InvalidDataException("Duplicate transfer path.");
    }

    public void Dispose()
    {
        archive?.Dispose();
        source.Dispose();
    }
}
