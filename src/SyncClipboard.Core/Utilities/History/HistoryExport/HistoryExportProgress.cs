namespace SyncClipboard.Core.Utilities.History.HistoryExport;

public sealed record HistoryExportProgress(int Processed, int Total, long WrittenBytes);
