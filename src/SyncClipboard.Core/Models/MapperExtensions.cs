using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Core.Models;

public static class MapperExtensions
{
    private static readonly TimeSpan RemoteDecisionThreshold = TimeSpan.FromMinutes(5);

    public static HistoryRecord ToHistoryRecord(this HistoryRecordDto dto)
    {
        return new HistoryRecord
        {
            Text = dto.Text,
            Type = dto.Type,
            FilePath = [],
            Hash = dto.Hash,
            Timestamp = dto.CreateTime,
            Stared = dto.Stared,
            Pinned = dto.Pinned,
            SyncStatus = HistorySyncStatus.Synced,
            LastModified = dto.LastModified,
            Version = dto.Version,
            IsDeleted = dto.IsDeleted,
            IsLocalFileReady = dto.Type == ProfileType.Text,
            Size = dto.Size
        };
    }

    public static void ApplyFromRemote(this HistoryRecord entity, HistoryRecordDto dto)
    {
        entity.Text = dto.Text;
        entity.Stared = dto.Stared;
        entity.Pinned = dto.Pinned;
        entity.Timestamp = dto.CreateTime;
        entity.LastModified = dto.LastModified;
        entity.Version = dto.Version;
        entity.IsDeleted = dto.IsDeleted;
        entity.Size = dto.Size;
        entity.SyncStatus = HistorySyncStatus.Synced;
    }

    public static bool ShouldUpdateFromRemote(this HistoryRecord entity, HistoryRecordDto dto)
    {
        var timeDiff = (dto.LastModified - entity.LastModified).Duration();
        if (timeDiff > RemoteDecisionThreshold)
        {
            return dto.LastModified > entity.LastModified;
        }
        return dto.Version > entity.Version;
    }

    public static bool IsLocalNewerThanRemote(this HistoryRecord entity, HistoryRecordDto dto)
    {
        var timeDiff = (dto.LastModified - entity.LastModified).Duration();
        if (timeDiff > RemoteDecisionThreshold)
        {
            return entity.LastModified > dto.LastModified;
        }
        return entity.Version > dto.Version;
    }

    public static HistoryRecordDto ToHistoryRecordDto(this HistoryRecord entity)
    {
        return new HistoryRecordDto
        {
            Hash = entity.Hash,
            Text = entity.Text,
            Type = entity.Type,
            CreateTime = entity.Timestamp,
            LastModified = entity.LastModified,
            LastAccessed = entity.LastModified,
            Stared = entity.Stared,
            Pinned = entity.Pinned,
            Size = entity.Size,
            Version = entity.Version,
            IsDeleted = entity.IsDeleted
        };
    }

    /// <summary>
    /// 将服务器返回的 HistoryRecordUpdateDto 的并发相关字段应用到本地实体上。
    /// 仅覆盖 Stared/Pinned/IsDeleted/LastModified/Version，并将 SyncStatus 置为 Synced。
    /// </summary>
    public static void ApplyFromServerUpdateDto(this HistoryRecord entity, HistoryRecordUpdateDto server)
    {
        if (server.Stared.HasValue) entity.Stared = server.Stared.Value;
        if (server.Pinned.HasValue) entity.Pinned = server.Pinned.Value;
        if (server.IsDelete.HasValue && server.IsDelete.Value) entity.IsDeleted = true;
        if (server.LastModified.HasValue) entity.LastModified = server.LastModified.Value.UtcDateTime;
        if (server.Version.HasValue) entity.Version = server.Version.Value;
        entity.SyncStatus = HistorySyncStatus.Synced;
    }
}

