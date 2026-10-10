namespace SyncClipboard.Core.Models.HistoryExport;

public sealed record HistoryExportSkipped(
    string ProfileId, HistoryExportRecord? Item, HistoryExportFailure Reason, string? Detail = null);
