using SyncClipboard.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.FileCacheManager;

namespace SyncClipboard.Core.Clipboard;

public static class ProfileExtentions
{
    public static async Task<string> GetOrCreateFileDataPath(this FileProfile profile, CancellationToken token)
    {
        var historyConfig = AppCore.Current.Services.GetRequiredService<ConfigManager>().GetConfig<HistoryConfig>();
        if (historyConfig.EnableHistory)
        {
            var historyFolder = Path.Combine(Env.HistoryFileFolder, await profile.GetHash(token));
            if (!Directory.Exists(historyFolder))
            {
                Directory.CreateDirectory(historyFolder);
            }
            return Path.Combine(historyFolder, profile.FileName);
        }
        else
        {
            return Path.Combine(Env.TemplateFileFolder, profile.FileName);
        }
    }

    public static async Task PrepareDataWithCache(this GroupProfile profile, CancellationToken token)
    {
        var cacheManager = AppCore.Current.Services.GetRequiredService<LocalFileCacheManager>();
        var cachedZipPath = await cacheManager.GetCachedFilePathAsync(nameof(GroupProfile), await profile.GetHash(token));
        if (!string.IsNullOrEmpty(cachedZipPath))
        {
            profile.FullPath = cachedZipPath;
            return;
        }

        var newDatePath = await profile.GetOrCreateFileDataPath(token);
        await profile.PrepareTransferFile(newDatePath, token);

        if (File.Exists(profile.FullPath))
        {
            await cacheManager.SaveCacheEntryAsync(nameof(GroupProfile), await profile.GetHash(token), profile.FullPath);
        }
    }

    public static ClipboardMetaInfomation GetMetaInfomation(this Profile profile)
    {
        return profile switch
        {
            ImageProfile ip => new() { Files = ip.FullPath is null ? [] : [ip.FullPath], Text = ip.FileName, OriginalType = ClipboardMetaInfomation.ImageType },
            GroupProfile gp => new() { Files = gp.Files },
            FileProfile fp => new() { Files = fp.FullPath is null ? [] : [fp.FullPath], Text = fp.FileName },
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

    public static async Task<HistoryRecord> ToHistoryRecord(this Profile profile, CancellationToken token)
    {
        var record = new HistoryRecord
        {
            Text = profile.Text,
            Type = profile.Type,
            Size = await profile.GetSize(token),
            Hash = await profile.GetHash(token),
            FilePath = profile switch
            {
                ImageProfile ip when ip.FullPath is not null => [ip.FullPath],
                FileProfile fp when fp.FullPath is not null => [fp.FullPath],
                GroupProfile gp when gp.Files is not null => gp.Files,
                _ => [],
            }
        };
        return record;
    }
}