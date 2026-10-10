namespace SyncClipboard.Core.Utilities.History.HistoryImport;

public sealed class HistoryImportResult
{
    public int ImportedCount { get; set; }
    public int RepairedCount { get; set; }
    public int ExistingCount { get; set; }
    public bool Canceled { get; set; }
    public List<HistoryImportFailure> Failures { get; } = [];
    public string? ReportPath { get; set; }
    public string? ReportError { get; set; }
}
