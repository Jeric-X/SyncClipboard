using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Server.Core.Models;
using System.Globalization;

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
            Hash = dto.Hash.ToUpperInvariant(),
            Timestamp = dto.CreateTime.UtcDateTime,
            Stared = dto.Starred,
            Pinned = dto.Pinned,
            SyncStatus = HistorySyncStatus.Synced,
            LastModified = dto.LastModified.UtcDateTime,
            LastAccessed = dto.LastAccessed.UtcDateTime,
            Version = dto.Version,
            IsDeleted = dto.IsDeleted,
            IsLocalFileReady = !dto.HasData,
            Size = dto.Size
        };
    }

    public static void ApplyChangesFromRemote(this HistoryRecord entity, HistoryRecordDto dto)
    {
        entity.Stared = dto.Starred;
        entity.Pinned = dto.Pinned;
        entity.Version = dto.Version;
        entity.IsDeleted = dto.IsDeleted;
    }

    public static void ApplyBasicFromRemote(this HistoryRecord entity, HistoryRecordDto dto)
    {
        entity.Text = dto.Text;
        entity.Timestamp = dto.CreateTime.UtcDateTime;
        entity.LastModified = dto.LastModified.UtcDateTime;
        entity.LastAccessed = dto.LastAccessed.UtcDateTime;
        entity.Size = dto.Size;
    }

    public static bool ShouldUpdateFromRemote(this HistoryRecord entity, HistoryRecordDto dto)
    {
        var timeDiff = (dto.LastModified.UtcDateTime - entity.LastModified).Duration();
        if (timeDiff > RemoteDecisionThreshold)
        {
            return dto.LastModified.UtcDateTime > entity.LastModified;
        }
        return dto.Version > entity.Version;
    }

    public static bool IsLocalNewerThanRemote(this HistoryRecord entity, HistoryRecordDto dto)
    {
        var timeDiff = (dto.LastModified.UtcDateTime - entity.LastModified).Duration();
        if (timeDiff > RemoteDecisionThreshold)
        {
            return entity.LastModified > dto.LastModified.UtcDateTime;
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
            LastAccessed = entity.LastAccessed,
            Starred = entity.Stared,
            Pinned = entity.Pinned,
            Size = entity.Size,
            Version = entity.Version,
            IsDeleted = entity.IsDeleted,
            HasData = !entity.IsLocalFileReady || entity.FilePath.Length > 0
        };
    }

    /// <summary>
    /// 将服务器返回的 HistoryRecordUpdateDto 的并发相关字段应用到本地实体上。
    /// 仅覆盖 Stared/Pinned/IsDeleted/LastModified/LastAccessed/Version，并将 SyncStatus 置为 Synced。
    /// </summary>
    public static void ApplyFromServerUpdateDto(this HistoryRecord entity, HistoryRecordUpdateDto server)
    {
        if (server.Starred.HasValue) entity.Stared = server.Starred.Value;
        if (server.Pinned.HasValue) entity.Pinned = server.Pinned.Value;
        if (server.IsDelete.HasValue && server.IsDelete.Value) entity.IsDeleted = true;
        if (server.LastModified.HasValue) entity.LastModified = server.LastModified.Value.UtcDateTime;
        if (server.LastAccessed.HasValue) entity.LastAccessed = server.LastAccessed.Value.UtcDateTime;
        if (server.Version.HasValue) entity.Version = server.Version.Value;
        entity.SyncStatus = HistorySyncStatus.Synced;
    }

    public static ClipboardMetaInfomation GetMetaInfomation(this ProfileLocalInfo info)
    {
        return new ClipboardMetaInfomation
        {
            Files = info.FilePaths,
            Text = info.Text,
        };
    }

    public static Profile ToProfile(this HistoryRecord historyRecord)
    {
        var profileEnv = AppCore.Current.Services.GetRequiredService<IProfileEnv>();
        return Profile.Create(profileEnv.GetHistoryPersistentDir(), new ProfilePersistentInfo
        {
            Text = historyRecord.Text,
            Type = historyRecord.Type,
            Size = historyRecord.Size,
            Hash = historyRecord.Hash,
            FilePaths = historyRecord.FilePath,
        });
    }
}

