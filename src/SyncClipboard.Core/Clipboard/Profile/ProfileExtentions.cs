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

    public static async Task<HistoryRecord> CreateHistoryRecord(this Profile profile, CancellationToken token)
    {
        switch (profile)
        {
            case ImageProfile ip:
                {
                    return new HistoryRecord
                    {
                        Type = ProfileType.Image,
                        Text = ip.FileName,
                        FilePath = ip.FullPath is null ? [] : [ip.FullPath],
                        Hash = await ip.GetHash(token),
                        Size = await ip.GetSize(token),
                    };
                }

            case GroupProfile gp:
                {
                    return new HistoryRecord
                    {
                        Type = ProfileType.Group,
                        Text = string.Join('\n', gp.Files ?? []),
                        FilePath = gp.Files ?? [],
                        Hash = await gp.GetHash(token),
                        Size = await gp.GetSize(token),
                    };
                }

            case FileProfile fp:
                {
                    return new HistoryRecord
                    {
                        Type = ProfileType.File,
                        Text = fp.FullPath ?? fp.FileName,
                        FilePath = [fp.FullPath ?? string.Empty],
                        Hash = await fp.GetHash(token),
                        Size = await fp.GetSize(token),
                    };
                }

            case TextProfile tp:
                {
                    byte[] inputBytes = System.Text.Encoding.Unicode.GetBytes(tp.Text);
                    byte[] hashBytes = System.Security.Cryptography.MD5.HashData(inputBytes);
                    var hash = Convert.ToHexString(hashBytes);
                    return new HistoryRecord
                    {
                        Type = ProfileType.Text,
                        Hash = hash,
                        Text = tp.Text,
                        Size = await tp.GetSize(token),
                    };
                }

            default:
                throw new NotImplementedException();
        }
    }
}