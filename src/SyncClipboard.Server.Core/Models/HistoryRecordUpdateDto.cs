namespace SyncClipboard.Server.Core.Models;

public class HistoryRecordUpdateDto
{
    public string? Text { get; set; }
    public bool? Stared { get; set; }
    public bool? Pinned { get; set; }
}
