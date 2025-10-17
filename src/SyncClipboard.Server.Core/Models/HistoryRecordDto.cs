using SyncClipboard.Abstract;

namespace SyncClipboard.Server.Core.Models;

public class HistoryRecordDto
{
    public string Hash { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public ProfileType Type { get; set; } = ProfileType.None;
    public string[] FilePath { get; set; } = [];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    public bool Stared { get; set; }
    public bool Pinned { get; set; }
    public long? Size { get; set; }
    // Mime removed from DTO; internal storage moved to entity extraMeta

    public static HistoryRecordDto FromEntity(HistoryRecordEntity e)
    {
        return new HistoryRecordDto
        {
            Hash = e.Hash,
            Text = e.Text,
            Type = e.Type,
            FilePath = e.FilePath,
            Timestamp = e.Timestamp,
            LastAccessed = e.LastAccessed,
            Stared = e.Stared,
            Pinned = e.Pinned,
            Size = e.Size
        };
    }

    public HistoryRecordEntity ToEntity()
    {
        return new HistoryRecordEntity
        {
            Hash = this.Hash,
            Text = this.Text,
            Type = this.Type,
            FilePath = this.FilePath ?? [],
            Timestamp = this.Timestamp,
            LastAccessed = this.LastAccessed,
            Stared = this.Stared,
            Pinned = this.Pinned,
            Size = this.Size
        };
    }
}
