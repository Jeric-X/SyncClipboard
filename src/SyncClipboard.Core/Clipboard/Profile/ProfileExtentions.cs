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

    public static ClipboardMetaInfomation GetMetaInfomation(this Profile profile)
    {
        return profile switch
        {
            ImageProfile ip => new() { Files = ip.FullPath is null ? [] : [ip.FullPath], Text = ip.Text, OriginalType = ClipboardMetaInfomation.ImageType },
            GroupProfile gp => new() { Files = gp.Files },
            FileProfile fp => new() { Files = fp.FullPath is null ? [] : [fp.FullPath], Text = fp.Text },
            TextProfile tp => new() { Text = tp.Text },
            _ => new ClipboardMetaInfomation(),
        };
    }

    public static Profile ToProfile(this HistoryRecord historyRecord)
    {
        bool ready = historyRecord.IsLocalFileReady;
        return historyRecord.Type switch
        {
            ProfileType.Text => new TextProfile(historyRecord.Text),
            ProfileType.File => new FileProfile(ready ? historyRecord.FilePath[0] : null, historyRecord.Text, historyRecord.Hash),
            ProfileType.Image => new ImageProfile(ready ? historyRecord.FilePath[0] : null, historyRecord.Text, historyRecord.Hash),
            ProfileType.Group => ready ? new GroupProfile(historyRecord.FilePath, historyRecord.Hash) : new GroupProfile(historyRecord.Hash),
            _ => new UnknownProfile(),
        };
    }

    public static void SetFilePath(this HistoryRecord record, Profile profile)
    {
        record.FilePath = profile switch
        {
            ImageProfile ip when ip.FullPath is not null => [ip.FullPath],
            FileProfile fp when fp.FullPath is not null => [fp.FullPath],
            GroupProfile gp when gp.Files is not null => gp.Files,
            _ => [],
        };
    }

    public static async Task<HistoryRecord> ToHistoryRecord(this Profile profile, CancellationToken token)
    {
        await profile.PreparePersistent(token);
        var record = new HistoryRecord
        {
            Text = profile.Text,
            Type = profile.Type,
            Size = await profile.GetSize(token),
            Hash = await profile.GetHash(token),
        };
        record.SetFilePath(profile);
        return record;
    }
}