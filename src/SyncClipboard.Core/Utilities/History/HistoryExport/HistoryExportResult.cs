namespace SyncClipboard.Core.Utilities.History.HistoryExport;

public sealed class HistoryExportResult
{
    public string? ArchivePath { get; set; }
    public string? ReportPath { get; set; }
    public string? ReportError { get; set; }
    public string? Error { get; set; }
    public bool Canceled { get; set; }
    public int ExportedCount { get; set; }
    public List<HistoryExportSkipped> Skipped { get; } = [];
    public int MissingCount => Skipped.Count(item => item.Reason == HistoryExportFailure.MissingFile);
    public int OtherCount => Skipped.Count - MissingCount;
}
