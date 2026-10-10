namespace SyncClipboard.Core.Models.HistoryExport;

public sealed record HistoryExportPlan(
    IReadOnlyList<HistoryExportRecord> Items, IReadOnlyList<HistoryExportSkipped> Skipped,
    long EstimatedBytes);
