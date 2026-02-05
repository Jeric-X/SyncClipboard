using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.FileCacheManager;
using SyncClipboard.Core.Utilities.Image;

namespace SyncClipboard.Core.Clipboard;

public abstract class ClipboardFactoryBase : IClipboardFactory
{
    protected abstract ILogger Logger { get; set; }
    protected abstract IServiceProvider ServiceProvider { get; set; }
    protected ConfigManager Config => ServiceProvider.GetRequiredService<ConfigManager>();
    private IProfileEnv ProfileEnv => ServiceProvider.GetRequiredService<IProfileEnv>();
    private LocalFileCacheManager FileCacheManager => ServiceProvider.GetRequiredService<LocalFileCacheManager>();

    public abstract Task<ClipboardMetaInfomation> GetMetaInfomation(CancellationToken ctk);
    public Task<Profile> CreateProfileFromMeta(ClipboardMetaInfomation metaInfomation, CancellationToken ctk)
    {
        return CreateProfileFromMeta(metaInfomation, true, ctk);
    }

    public async Task<Profile> CreateProfileFromMeta(ClipboardMetaInfomation metaInfomation, bool contentControl, CancellationToken ctk)
    {
        if (metaInfomation.Files != null && metaInfomation.Files.Length >= 1)
        {
            var filename = metaInfomation.Files[0];
            if (metaInfomation.Files.Length == 1 && File.Exists(filename))
            {
                if (ImageHelper.FileIsImage(filename))
                {
                    return new ImageProfile(filename);
                }
                return new FileProfile(filename, null, null);
            }
            else
            {
                return new GroupProfile(metaInfomation.Files, contentControl ? Config.GetConfig<FileFilterConfig>() : null);
            }
        }

        if (metaInfomation.Text != null)
        {
            return new TextProfile(metaInfomation.Text);
        }

        if (metaInfomation.Image != null)
        {
            return await CreateImageProfileWithCache(metaInfomation.Image, ctk);
        }

        return new UnknownProfile();
    }

    private async Task<ImageProfile> CreateImageProfileWithCache(IClipboardImage image, CancellationToken ctk)
    {
        var hash = image.GetHashCode().ToString();
        var cachedPath = await FileCacheManager.GetCachedFilePathAsync(nameof(IClipboardImage), hash, ctk);
        if (cachedPath is not null)
        {
            return new ImageProfile(cachedPath);
        }

        var tempPath = Path.Combine(Env.TemplateFileFolder, ImageProfile.CreateImageFileName());
        await image.Save(tempPath, ctk);

        var imageProfile = new ImageProfile(tempPath);

        var profileHash = await imageProfile.GetHash(ctk);
        var workingDir = imageProfile.CreateWorkingDir(ProfileEnv.GetPersistentDir(), profileHash);
        var dataPath = Path.Combine(workingDir, Path.GetFileName(tempPath));

        if (tempPath != dataPath)
        {
            await Task.Run(() => File.Move(tempPath, dataPath, true), ctk).WaitAsync(ctk);
            await imageProfile.SetTransferData(dataPath, false, ctk);
        }

        await FileCacheManager.SaveCacheEntryAsync(nameof(IClipboardImage), hash, dataPath, ctk);
        return imageProfile;
    }

    public async Task<Profile> CreateProfileFromLocal(CancellationToken ctk)
    {
        var meta = await GetMetaInfomation(ctk);
        return await CreateProfileFromMeta(meta, ctk);
    }
}
