using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.ViewModels.Sub;

public partial class HistoryRecordVM(HistoryRecord record) : ObservableObject
{
    private readonly int id = record.ID;

    [ObservableProperty]
    private string text = record.Text;
    public ProfileType Type { get; set; } = record.Type;
    [ObservableProperty]
    private string[] filePath = record.FilePath;
    public string Hash { get; set; } = record.Hash;
    [ObservableProperty]
    private DateTime timestamp = record.Timestamp;
    [ObservableProperty]
    private bool stared = record.Stared;
    [ObservableProperty]
    private bool pinned = record.Pinned;
    // Sync state used by UI to indicate server/local sync status
    [ObservableProperty]
#if DEBUG
    private SyncStatus syncState = GetRandomSyncStatus();
#else
    private SyncStatus syncState = SyncStatus.Synced;
#endif

    public HistoryRecord ToHistoryRecord()
    {
        return new HistoryRecord
        {
            ID = id,
            Text = Text,
            Type = Type,
            FilePath = FilePath,
            Hash = Hash,
            Timestamp = Timestamp,
            Stared = Stared,
            Pinned = Pinned
        };
    }

    public void Update(HistoryRecordVM record)
    {
        Text = record.Text;
        FilePath = record.FilePath;
        Timestamp = record.Timestamp;
        Stared = record.Stared;
        Pinned = record.Pinned;
        // For testing: randomize SyncState when update is called so UI can show different states
#if DEBUG
        try
        {
            SyncState = GetRandomSyncStatus();
        }
        catch
        {
            SyncState = SyncStatus.Synced;
        }
#endif
    }

#if DEBUG
    private static SyncStatus GetRandomSyncStatus()
    {
        var values = (SyncStatus[])Enum.GetValues(typeof(SyncStatus));
        // Use Guid-based seed for better distribution across quick successive creations
        var rnd = new Random(Guid.NewGuid().GetHashCode());
        return values[rnd.Next(values.Length)];
    }
#endif

    public override bool Equals(object? obj)
    {
        if (obj is not HistoryRecordVM other)
        {
            return false;
        }

        if (Type != other.Type)
        {
            return false;
        }

        return Hash == other.Hash;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Hash);
    }

    public static bool operator ==(HistoryRecordVM? left, HistoryRecordVM? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.Equals(right);
    }

    public static bool operator !=(HistoryRecordVM? left, HistoryRecordVM? right)
    {
        return !(left == right);
    }
}