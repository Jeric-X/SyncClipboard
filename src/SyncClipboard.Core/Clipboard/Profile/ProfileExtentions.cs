using Microsoft.Extensions.DependencyInjection;
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
}