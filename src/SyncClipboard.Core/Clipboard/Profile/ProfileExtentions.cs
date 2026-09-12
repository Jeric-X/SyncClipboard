using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Utilities.FileCacheManager;
using SyncClipboard.Shared.Models;

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
            await BindCachedTransferData(profile, cachedFile, token);
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

    internal static async Task BindCachedTransferData(
        Profile profile,
        FileHashInfo cachedFile,
        CancellationToken token)
    {
        await profile.SetTransferData(
            cachedFile.Path,
            cachedFile.Hash,
            verify: false,
            token);
    }
}
