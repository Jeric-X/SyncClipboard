namespace SyncClipboard.Core.Utilities.History.HistoryImport;

public sealed class HistoryImportResult
{
    public int ImportedCount { get; set; }
    public int RepairedCount { get; set; }
    public int ExistingCount { get; set; }
    public string? Error { get; set; }
    public bool Canceled { get; set; }
    public int FailureCount { get; set; }
    public string? ReportPath { get; set; }
    public string? ReportError { get; set; }
}
