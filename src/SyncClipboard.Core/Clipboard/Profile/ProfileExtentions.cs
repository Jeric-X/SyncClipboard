using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.FileCacheManager;

namespace SyncClipboard.Core.Clipboard;

public static class ProfileExtentions
{
    public static async Task<string?> PrepareDataWithCache(this Profile profile, CancellationToken token)
    {
        var cacheManager = AppCore.Current.Services.GetRequiredService<LocalFileCacheManager>();
        var cachedFilePath = await cacheManager.GetCachedFilePathAsync(profile.Type.ToString(), await profile.GetHash(token));
        if (!string.IsNullOrEmpty(cachedFilePath))
        {
            await profile.SetTranseferData(cachedFilePath, false, token);
            return cachedFilePath;
        }

        var path = await profile.PrepareTransferData(token);

        if (File.Exists(path))
        {
            await cacheManager.SaveCacheEntryAsync(profile.Type.ToString(), await profile.GetHash(token), path);
        }
        return path;
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
        return Profile.Create(new ProfilePersistentInfo
        {
            Text = historyRecord.Text,
            Type = historyRecord.Type,
            Size = historyRecord.Size,
            Hash = historyRecord.Hash,
            FilePaths = historyRecord.FilePath,
        });
    }

    public static void SetFilePath(this HistoryRecord record, ProfilePersistentInfo profileEntity)
    {
        record.FilePath = profileEntity.FilePaths;
    }

    public static async Task<HistoryRecord> ToHistoryRecord(this Profile profile, CancellationToken token)
    {
        var profileEntity = await profile.Persistentize(token);
        var record = new HistoryRecord
        {
            Text = profileEntity.Text,
            Type = profileEntity.Type,
            Size = profileEntity.Size,
            Hash = profileEntity.Hash,
        };
        record.SetFilePath(profileEntity);
        return record;
    }
}