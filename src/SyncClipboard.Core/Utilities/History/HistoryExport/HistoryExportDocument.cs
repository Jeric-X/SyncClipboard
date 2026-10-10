namespace SyncClipboard.Core.Utilities.History.HistoryExport;

public sealed record HistoryExportDocument(
    string Format, int FormatVersion, string AppVersion, DateTime ExportedAt, IReadOnlyList<HistoryExportRecord> Records);
