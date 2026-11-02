namespace SyncClipboard.Server.Core.Models;

public class HistoryRecordCreateDto
{
    public string[]? Tags { get; set; }
    public string? Text { get; set; }
    public bool? Stared { get; set; }
    public bool? Pinned { get; set; }
    public DateTime? Timestamp { get; set; }
}
