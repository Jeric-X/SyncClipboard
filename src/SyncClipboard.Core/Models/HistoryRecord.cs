namespace SyncClipboard.Core.Models;

public class HistoryRecord
{
    public int ID { get; set; }
    public string Text { get; set; } = string.Empty;
    public ProfileType Type { get; set; } = ProfileType.None;
    public string[] FilePath { get; set; } = [];
    public string Hash { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool Stared { get; set; }
    public bool Pinned { get; set; }
    public HistorySyncStatus SyncStatus { get; set; } = HistorySyncStatus.LocalOnly;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
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

        return Hash == other.Hash;
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