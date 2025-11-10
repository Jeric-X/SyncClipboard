namespace SyncClipboard.Server.Core.Models;

public class HistoryRecordUpdateDto
{
    public bool? Stared { get; set; }
    public bool? Pinned { get; set; }
    public bool? IsDelete { get; set; }
    public int? Version { get; set; }
    public DateTimeOffset? LastModified { get; set; }
}
