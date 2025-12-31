namespace SyncClipboard.Server.Core.Models;

public class HistoryRecordUpdateDto
{
    public bool? Starred { get; set; }
    public bool? Pinned { get; set; }
    public bool? IsDelete { get; set; }
    public int? Version { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? LastAccessed { get; set; }
}
