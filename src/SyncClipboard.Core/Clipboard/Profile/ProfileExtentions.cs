using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Utilities.FileCacheManager;

namespace SyncClipboard.Core.Clipboard;

public static class ProfileExtentions
{
    public static async Task<FileHashInfo?> PrepareDataWithCache(this Profile profile, CancellationToken token)
    {
        var cacheManager = AppCore.Current.Services.GetRequiredService<LocalFileCacheManager>();
        var cachedFile = await cacheManager.GetCachedFileInfoAsync(
            profile.Type.ToString(),
            await profile.GetHash(token),
            token);
        if (cachedFile is not null)
        {
            await profile.SetTransferData(cachedFile, false, token);
            return cachedFile;
        }

        var profileEnv = AppCore.Current.Services.GetRequiredService<IProfileEnv>();
        var file = await profile.PrepareTransferData(profileEnv.GetPersistentDir(), token);

        if (file is not null)
        {
            await cacheManager.SaveCacheEntryAsync(profile.Type.ToString(), await profile.GetHash(token), file, token);
        }
        return file;
    }
}
