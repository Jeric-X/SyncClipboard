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