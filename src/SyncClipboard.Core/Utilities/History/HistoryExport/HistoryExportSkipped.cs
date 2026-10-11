namespace SyncClipboard.Core.Utilities.History.HistoryExport;

public sealed record HistoryExportSkipped(
    string ProfileId, HistoryExportRecord? Item, HistoryExportFailure Reason, string? Detail = null);
