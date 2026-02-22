using Microsoft.EntityFrameworkCore;

namespace SyncClipboard.Core.Models;

public class HistoryRecord
{
    public int ID { get; set; }
    public string Text { get; set; } = string.Empty;
    public ProfileType Type { get; set; } = ProfileType.None;
    public string[] FilePath { get; set; } = [];
    public string Hash { get; set; } = string.Empty;

    public DateTime timestamp = DateTime.UtcNow;
    [BackingField(nameof(timestamp))]
    public DateTime Timestamp
    {
        get => DateTimePropertyHelper.GetDateTimeProperty(ref timestamp);
        set => DateTimePropertyHelper.SetDateTimeProperty(value, ref timestamp);
    }

    public bool Stared { get; set; }
    public bool Pinned { get; set; }
    public HistorySyncStatus SyncStatus { get; set; } = HistorySyncStatus.LocalOnly;

    public DateTime lastModified = DateTime.UtcNow;
    [BackingField(nameof(lastModified))]
    public DateTime LastModified
    {
        get => DateTimePropertyHelper.GetDateTimeProperty(ref lastModified);
        set => DateTimePropertyHelper.SetDateTimeProperty(value, ref lastModified);
    }

    public DateTime lastAccessed = DateTime.UtcNow;
    [BackingField(nameof(lastAccessed))]
    public DateTime LastAccessed
    {
        get => DateTimePropertyHelper.GetDateTimeProperty(ref lastAccessed);
        set => DateTimePropertyHelper.SetDateTimeProperty(value, ref lastAccessed);
    }

    public int Version { get; set; } = 0;
    public bool IsDeleted { get; set; } = false;
    public bool IsLocalFileReady { get; set; } = true;
    public long Size { get; set; } = 0;
    public string From { get; set; } = string.Empty;

    public override bool Equals(object? obj)
    {
        if (obj is not HistoryRecord other)
        {
            return false;
        }

        if (Type != other.Type)
        {
            return false;
        }

        if (Type == ProfileType.Text)
        {
            return Text == other.Text;
        }

        return string.Equals(Hash, other.Hash, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Text, Hash);
    }

    public static bool operator ==(HistoryRecord? left, HistoryRecord? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.Equals(right);
    }

    public static bool operator !=(HistoryRecord? left, HistoryRecord? right)
    {
        return !(left == right);
    }
}