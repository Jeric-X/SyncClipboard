namespace SyncClipboard.Server.Core.Models;

public static class Mapper
{
    public static async Task<HistoryRecordEntity> ToHistoryEntity(this Profile profile, string persistentDir, string userId, CancellationToken token)
    {
        var profileEntity = await profile.Persist(persistentDir, token).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var entity = new HistoryRecordEntity
        {
            UserId = userId,
            Size = profileEntity.Size,
            Hash = profileEntity.Hash,
            Type = profileEntity.Type,
            Text = profileEntity.Text,
            CreateTime = now,
            LastAccessed = now,
            LastModified = now,
            Stared = false,
            Pinned = false,
            TransferDataFile = profileEntity.TransferDataFile ?? string.Empty,
            ExtraData = null,
            FilePaths = profileEntity.FilePaths,
        };

        return entity;
    }

    public static Profile ToProfile(this HistoryRecordEntity entity, string persistentDir)
    {
        var persistentInfo = new ProfilePersistentInfo
        {
            Type = entity.Type,
            Text = entity.Text,
            Size = entity.Size,
            Hash = entity.Hash,
            TransferDataFile = string.IsNullOrEmpty(entity.TransferDataFile) ? null : entity.TransferDataFile,
            FilePaths = entity.FilePaths
        };
        return Profile.Create(persistentDir, persistentInfo);
    }

    public static HistoryRecordUpdateDto ToUpdateDto(this HistoryRecordDto s)
    {
        return new HistoryRecordUpdateDto
        {
            Starred = s.Starred,
            Pinned = s.Pinned,
            IsDelete = s.IsDeleted,
            Version = s.Version,
            LastModified = s.LastModified.ToUniversalTime(),
            LastAccessed = s.LastAccessed.ToUniversalTime()
        };
    }
}

