using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public bool Stared { get; set; }
    public bool Pinned { get; set; }
    public string From { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public string? ExtraData { get; set; }
}
