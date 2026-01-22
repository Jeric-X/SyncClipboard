using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Utilities.FileCacheManager;

namespace SyncClipboard.Core.Clipboard;

public static class ProfileExtentions
{
    public static async Task<string?> PrepareDataWithCache(this Profile profile, CancellationToken token)
    {
        var cacheManager = AppCore.Current.Services.GetRequiredService<LocalFileCacheManager>();
        var cachedFilePath = await cacheManager.GetCachedFilePathAsync(profile.Type.ToString(), await profile.GetHash(token), token);
        if (!string.IsNullOrEmpty(cachedFilePath))
        {
            await profile.SetTransferData(cachedFilePath, false, token);
            return cachedFilePath;
        }

        var profileEnv = AppCore.Current.Services.GetRequiredService<IProfileEnv>();
        var path = await profile.PrepareTransferData(profileEnv.GetPersistentDir(), token);

        if (File.Exists(path))
        {
            await cacheManager.SaveCacheEntryAsync(profile.Type.ToString(), await profile.GetHash(token), path, token);
        }
        return path;
    }
}