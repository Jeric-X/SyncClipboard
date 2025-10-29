namespace SyncClipboard.Server.Core.Models;

public class HistoryRecordDto
{
    public string Hash { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public ProfileType Type { get; set; } = ProfileType.None;
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    public bool Stared { get; set; }
    public bool Pinned { get; set; }
    public long Size { get; set; }

    public static HistoryRecordDto FromEntity(HistoryRecordEntity e)
    {
        return new HistoryRecordDto
        {
            Hash = e.Hash,
            Text = e.Text,
            Type = e.Type,
            CreateTime = e.CreateTime,
            LastModified = e.LastModified,
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
            CreateTime = this.CreateTime,
            LastAccessed = this.LastModified,
            Stared = this.Stared,
            Pinned = this.Pinned,
            Size = this.Size
        };
    }
}
