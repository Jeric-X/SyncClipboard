using SyncClipboard.Core.Utilities.History.HistoryExport;
using System.IO.Compression;

namespace SyncClipboard.Core.Utilities.History.HistoryImport;

public sealed class HistoryImportPlan(string path, FileStream source, ZipArchive? archive, HistoryExportDocument document) : IDisposable
{
    public string Path { get; } = path;
    public IReadOnlyList<HistoryExportRecord> Records { get; } = document.Records;
    private readonly Dictionary<string, ZipArchiveEntry?> entries = IndexEntries(archive);

    private static Dictionary<string, ZipArchiveEntry?> IndexEntries(ZipArchive? archive)
    {
        var entries = new Dictionary<string, ZipArchiveEntry?>(StringComparer.Ordinal);
        if (archive is not null)
        {
            foreach (var entry in archive.Entries)
            {
                if (!entries.TryAdd(entry.FullName, entry))
                    entries[entry.FullName] = null;
            }
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
