namespace SyncClipboard.Core.Utilities.History.HistoryExport;

public enum HistoryExportFailure
{
    MissingFile,
    InvalidData,
    ReadFailed,
    RecordRemoved,
    Unsupported,
    Canceled,
    ArchiveFailed
}
