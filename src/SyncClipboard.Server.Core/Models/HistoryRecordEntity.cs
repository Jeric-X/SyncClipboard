using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

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
    public string FilePathJson { get; set; } = JsonSerializer.Serialize(Array.Empty<string>());

    [NotMapped]
    public string[] FilePath
    {
        get => string.IsNullOrEmpty(FilePathJson) ? [] : JsonSerializer.Deserialize<string[]>(FilePathJson) ?? [];
        set => FilePathJson = JsonSerializer.Serialize(value ?? []);
    }

    public string Hash { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public bool Stared { get; set; }
    public bool Pinned { get; set; }
    public string? ExtraData { get; set; }
}
