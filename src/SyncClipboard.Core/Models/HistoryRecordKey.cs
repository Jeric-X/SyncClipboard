namespace SyncClipboard.Core.Models;

/// <summary>
/// Stable identity for a history record.  View models are replaced while the
/// history list is paged, so selection must not be tied to their instances.
/// </summary>
public readonly record struct HistoryRecordKey(ProfileType Type, string Hash)
{
    public static HistoryRecordKey From(HistoryRecord record) => new(record.Type, record.Hash);
}
