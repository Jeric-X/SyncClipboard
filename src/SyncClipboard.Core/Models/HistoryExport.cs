namespace SyncClipboard.Core.Models;

public sealed record HistoryExportItem(
    ProfilePersistentInfo Content, DateTime Timestamp, DateTime LastModified, DateTime LastAccessed,
    bool Starred, bool Pinned, string From)
{
    public string ProfileId => Profile.GetProfileId(Content.Type, Content.Hash.ToUpperInvariant());

    public static HistoryExportItem FromRecord(HistoryRecord record) => new(
        new ProfilePersistentInfo
        {
            Type = record.Type,
            Text = record.Text,
            Hash = record.Hash,
            Size = record.Size,
            FilePaths = [.. record.FilePath],
            TransferDataFile = record.TransferDataFile,
            TransferDataHash = record.TransferDataHash
        }, record.Timestamp, record.LastModified, record.LastAccessed, record.Stared, record.Pinned, record.From);
}

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

public sealed record HistoryExportSkipped(
    string ProfileId, HistoryExportItem? Item, HistoryExportFailure Reason, string? Detail = null);

public sealed record HistoryExportPlan(
    IReadOnlyList<HistoryExportItem> Items, IReadOnlyList<HistoryExportSkipped> Skipped,
    long EstimatedBytes) : IDisposable
{
    private readonly HashSet<string> _generatedFiles = [];

    internal void TrackGeneratedFile(string path) => _generatedFiles.Add(path);

    public void Dispose()
    {
        List<Exception> errors = [];
        foreach (var path in _generatedFiles.ToArray())
        {
            try
            {
                File.Delete(path);
                _generatedFiles.Remove(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(new IOException($"Failed to remove generated transfer file: {path}", ex));
            }
        }
        if (errors.Count > 0)
            throw new AggregateException(errors);
    }
}

public sealed record HistoryExportProgress(int Processed, int Total, long WrittenBytes);

public sealed class HistoryExportResult
{
    public string? ArchivePath { get; set; }
    public string? ReportPath { get; set; }
    public string? ReportError { get; set; }
    public string? Error { get; set; }
    public bool Canceled { get; set; }
    public int ExportedCount { get; set; }
    public List<HistoryExportSkipped> Skipped { get; } = [];
    public int MissingCount => Skipped.Count(item => item.Reason == HistoryExportFailure.MissingFile);
    public int OtherCount => Skipped.Count - MissingCount;
}

public sealed record HistoryExportTransferData(string Name, string Path, long Size, string Sha256);

public sealed record HistoryExportRecord(
    string Type, string Hash, string Text, DateTime Timestamp, DateTime LastModified, DateTime LastAccessed,
    bool Starred, bool Pinned, string From, bool HasTransferData, HistoryExportTransferData? TransferData);

public sealed record HistoryExportDocument(
    string Format, int FormatVersion, string AppVersion, DateTime ExportedAt, IReadOnlyList<HistoryExportRecord> Records);
