using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using SyncClipboard.Abstract;

namespace SyncClipboard.Server.Core.Models;

[Table("HistoryRecords")]
public class HistoryRecordEntity
{
    [Key]
    public int ID { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public ProfileType Type { get; set; } = ProfileType.None;

    // Stored as JSON in DB for portability
    public string FilePathJson { get; set; } = JsonSerializer.Serialize(Array.Empty<string>());

    [NotMapped]
    public string[] FilePath
    {
        get => string.IsNullOrEmpty(FilePathJson) ? [] : JsonSerializer.Deserialize<string[]>(FilePathJson) ?? [];
        set => FilePathJson = JsonSerializer.Serialize(value ?? []);
    }

    public string Hash { get; set; } = Guid.NewGuid().ToString();

    // When this history record was created or originally saved
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // When this record was last accessed from server side (e.g., downloaded)
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;

    public bool Stared { get; set; }
    public bool Pinned { get; set; }

    // Optional metadata (size, mime, etc.)
    public long? Size { get; set; }
    public string? Mime { get; set; }
    // ExtraMeta: reserved JSON string for arbitrary metadata
    public string? ExtraMeta { get; set; }
}
