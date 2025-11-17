using System.Text.Json.Serialization;

namespace SyncClipboard.Server.Core.Models;

public class HistoryRecordDto
{
    public string Hash { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProfileType Type { get; set; } = ProfileType.None;
    public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastAccessed { get; set; } = DateTimeOffset.UtcNow;
    public bool Starred { get; set; }
    public bool Pinned { get; set; }
    public long Size { get; set; }
    public int Version { get; set; } = 0;
    public bool IsDeleted { get; set; } = false;

    public static HistoryRecordDto FromEntity(HistoryRecordEntity e)
    {
        return new HistoryRecordDto
        {
            Hash = e.Hash,
            Text = e.Text,
            Type = e.Type,
            CreateTime = new DateTimeOffset(e.CreateTime, TimeSpan.Zero),
            LastModified = new DateTimeOffset(e.LastModified, TimeSpan.Zero),
            LastAccessed = new DateTimeOffset(e.LastAccessed, TimeSpan.Zero),
            Starred = e.Stared,
            Pinned = e.Pinned,
            Size = e.Size,
            Version = e.Version,
            IsDeleted = e.IsDeleted
        };
    }

    public HistoryRecordEntity ToEntity()
    {
        return new HistoryRecordEntity
        {
            Hash = this.Hash,
            Text = this.Text,
            Type = this.Type,
            CreateTime = this.CreateTime.UtcDateTime,
            LastAccessed = this.LastAccessed.UtcDateTime,
            LastModified = this.LastModified.UtcDateTime,
            Stared = this.Starred,
            Pinned = this.Pinned,
            Size = this.Size,
            Version = this.Version,
            IsDeleted = this.IsDeleted
        };
    }
}
