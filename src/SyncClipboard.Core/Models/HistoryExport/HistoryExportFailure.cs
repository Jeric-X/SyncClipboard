namespace SyncClipboard.Core.Models.HistoryExport;

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
