using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SyncClipboard.Shared.Models;

namespace SyncClipboard.Server.Core.Models;

[Table("HistoryRecords")]
public class HistoryRecordEntity
{
    [Key]
    public int ID { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ProfileType Type { get; set; } = ProfileType.None;
    public string Text { get; set; } = string.Empty;
    public long Size { get; set; } = 0;
    public string TransferDataFile { get; set; } = string.Empty;
    public string TransferDataSha256 { get; set; } = string.Empty;
    public string TransferDataMd5 { get; set; } = string.Empty;
    public string[] FilePaths { get; set; } = [];
    public string Hash { get; set; } = Guid.NewGuid().ToString();
    public DateTime createTime = DateTime.UtcNow;
    public DateTime lastAccessed = DateTime.UtcNow;
    public DateTime lastModified = DateTime.UtcNow;
    public DateTime CreateTime
    {
        get => DateTimePropertyHelper.GetDateTimeProperty(ref createTime);
        set => DateTimePropertyHelper.SetDateTimeProperty(value, ref createTime);
    }
    [BackingField(nameof(lastAccessed))]
    public DateTime LastAccessed
    {
        get => DateTimePropertyHelper.GetDateTimeProperty(ref lastAccessed);
        set => DateTimePropertyHelper.SetDateTimeProperty(value, ref lastAccessed);
    }
    [BackingField(nameof(lastModified))]
    public DateTime LastModified
    {
        get => DateTimePropertyHelper.GetDateTimeProperty(ref lastModified);
        set => DateTimePropertyHelper.SetDateTimeProperty(value, ref lastModified);
    }
    public bool Stared { get; set; }
    public bool Pinned { get; set; }
    public string From { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public string? ExtraData { get; set; }
    public int Version { get; set; } = 0;
    public bool IsDeleted { get; set; } = false;
}
