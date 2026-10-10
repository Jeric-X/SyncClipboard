namespace SyncClipboard.Core.Utilities.History.HistoryExport;

public sealed record HistoryExportPlan(
    IReadOnlyList<HistoryExportRecord> Items, IReadOnlyList<HistoryExportSkipped> Skipped,
    long EstimatedBytes);
