using SyncClipboard.Core.Utilities.History.HistoryExport;
using System.IO.Compression;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.History.HistoryImport;

public sealed class HistoryImportPlan(string path, FileStream source, ZipArchive? archive, HistoryImportDocument document,
    CancellationToken token) : IDisposable
{
    public string Path { get; } = path;
    public IReadOnlyList<HistoryExportRecord?> Records { get; } = new HistoryImportRecords(document.Records);
    private readonly Dictionary<string, ZipArchiveEntry?> entries = IndexEntries(archive?.Entries ?? [], new HistoryImportRecords(document.Records), token);

    internal static Dictionary<string, ZipArchiveEntry?> IndexEntries(
        IEnumerable<ZipArchiveEntry> archiveEntries, IReadOnlyList<HistoryExportRecord?> records, CancellationToken token)
    {
        var referencedPaths = GetReferencedPaths(records, token);

        var entries = new Dictionary<string, ZipArchiveEntry?>(StringComparer.Ordinal);
        foreach (var entry in archiveEntries)
        {
            token.ThrowIfCancellationRequested();
            if (referencedPaths.Contains(entry.FullName) && !entries.TryAdd(entry.FullName, entry))
                entries[entry.FullName] = null;
        }
        return entries;
    }

    internal static HashSet<string> GetReferencedPaths(IReadOnlyList<HistoryExportRecord?> records, CancellationToken token)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < records.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (records[i]?.TransferData is { } transfer)
                    paths.Add(transfer.Path);
            }
            catch (JsonException) { } // Conversion failures are reported at their original record index during import.
        }
        return paths;
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
