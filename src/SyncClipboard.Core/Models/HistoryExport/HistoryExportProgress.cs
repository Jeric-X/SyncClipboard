namespace SyncClipboard.Core.Models.HistoryExport;

public sealed record HistoryExportProgress(int Processed, int Total, long WrittenBytes);
