using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Utilities.FileCacheManager;

namespace SyncClipboard.Core.Clipboard;

public static class ProfileExtentions
{
    public static async Task<string?> PrepareDataWithCache(this Profile profile, CancellationToken token)
    {
        var cacheManager = AppCore.Current.Services.GetRequiredService<LocalFileCacheManager>();
        var cachedFile = await cacheManager.GetValidatedCachedFileAsync(
            profile.Type.ToString(),
            await profile.GetHash(token),
            token);
        if (cachedFile is not null)
        {
            await BindCachedTransferData(profile, cachedFile, token);
            return cachedFile.FilePath;
        }

        var profileEnv = AppCore.Current.Services.GetRequiredService<IProfileEnv>();
        var path = await profile.PrepareTransferData(profileEnv.GetPersistentDir(), token);

        if (File.Exists(path))
        {
            await cacheManager.SaveCacheEntryAsync(profile.Type.ToString(), await profile.GetHash(token), path, token);
        }
        return path;
    }

    internal static async Task BindCachedTransferData(
        Profile profile,
        ValidatedCachedFile cachedFile,
        CancellationToken token)
    {
        await profile.SetTransferData(
            cachedFile.FilePath,
            cachedFile.TransferDataHash,
            verify: false,
            token);
    }
}
