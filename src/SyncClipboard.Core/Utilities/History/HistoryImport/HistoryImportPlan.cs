using SyncClipboard.Core.Utilities.History.HistoryExport;
using System.IO.Compression;

namespace SyncClipboard.Core.Utilities.History.HistoryImport;

public sealed class HistoryImportPlan(string path, FileStream source, ZipArchive? archive, HistoryExportDocument document) : IDisposable
{
    public string Path { get; } = path;
    public IReadOnlyList<HistoryExportRecord> Records { get; } = document.Records;
    internal ZipArchive? Archive { get; } = archive;
    public void Dispose()
    {
        Archive?.Dispose();
        source.Dispose();
    }
}
