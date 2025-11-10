namespace SyncClipboard.Server.Core.Models;

public static class Mapper
{
    public static async Task<HistoryRecordEntity> ToHistoryEntity(this Profile profile, string userId, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var entity = new HistoryRecordEntity
        {
            UserId = userId,
            Size = await profile.GetSize(token).ConfigureAwait(false),
            Hash = await profile.GetHash(token).ConfigureAwait(false),
            Type = profile.Type,
            Text = profile.Text,
            CreateTime = now,
            LastAccessed = now,
            LastModified = now,
            Stared = false,
            Pinned = false,
            TransferDataFile = string.Empty,
            ExtraData = null
        };

        if (profile is FileProfile fp)
        {
            entity.TransferDataFile = fp.FullPath ?? string.Empty;
            if (profile is GroupProfile gp)
            {
                entity.FilePaths = gp.Files;
            }
        }

        return entity;
    }

    public static Profile ToProfile(this HistoryRecordEntity entity)
    {
        return entity.Type switch
        {
            ProfileType.Text => new TextProfile(entity.Text),
            ProfileType.File => new FileProfile(entity.TransferDataFile, null, entity.Hash),
            ProfileType.Image => new ImageProfile(entity.TransferDataFile, null, entity.Hash),
            ProfileType.Group => new GroupProfile(entity.FilePaths, entity.Hash, entity.TransferDataFile),
            _ => new UnknownProfile(),
        };
    }

    public static HistoryRecordUpdateDto ToUpdateDto(this HistoryRecordDto s)
    {
        return new HistoryRecordUpdateDto
        {
            Stared = s.Stared,
            Pinned = s.Pinned,
            IsDelete = s.IsDeleted,
            Version = s.Version,
            LastModified = new DateTimeOffset(s.LastModified.ToUniversalTime(), TimeSpan.Zero)
        };
    }
}

