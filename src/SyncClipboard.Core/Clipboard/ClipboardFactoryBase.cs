using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Image;

namespace SyncClipboard.Core.Clipboard;

public abstract class ClipboardFactoryBase : IClipboardFactory
{
    protected abstract ILogger Logger { get; set; }
    protected abstract IServiceProvider ServiceProvider { get; set; }
    protected ConfigManager Config => ServiceProvider.GetRequiredService<ConfigManager>();

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
                return await GroupProfile.Create(metaInfomation.Files, contentControl ? Config.GetConfig<FileFilterConfig>() : null);
            }
        }

        if (metaInfomation.Text != null)
        {
            return new TextProfile(metaInfomation.Text);
        }

        if (metaInfomation.Image != null)
        {
            return await CreateImageProfile(metaInfomation.Image, ctk);
        }

        return new UnknownProfile();
    }

    private async Task<ImageProfile> CreateImageProfile(IClipboardImage image, CancellationToken token)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                var tempPath = await Task.Run(() => SaveImageToFile(image)).WaitAsync(token);
                var imageProfile = new ImageProfile(tempPath, null, null);

                // 如果启用历史记录，移动文件到历史记录文件夹
                var historyConfig = Config.GetConfig<HistoryConfig>();
                if (historyConfig.EnableHistory)
                {
                    var historyFolder = Path.Combine(Env.HistoryFileFolder, await imageProfile.GetHash(token));
                    Directory.CreateDirectory(historyFolder);

                    var fileName = Path.GetFileName(tempPath);
                    var historyPath = Path.Combine(historyFolder, fileName);

                    if (tempPath != historyPath)
                    {
                        File.Move(tempPath, historyPath);
                        imageProfile.FullPath = historyPath;
                    }
                }

                return imageProfile;
            }
            catch when (!token.IsCancellationRequested)
            {
                Logger.Write($"SaveImageToFile wrong time {i + 1}");
                if (i > 5)
                    throw;
            }
            await Task.Delay(100, token);
        }
    }

    private static string SaveImageToFile(IClipboardImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!Directory.Exists(Env.ImageTemplateFolder))
        {
            Directory.CreateDirectory(Env.ImageTemplateFolder);
        }
        var filePath = ImageProfile.CreateNewDataFileName();
        image.Save(filePath);
        return filePath;
    }

    public Task<Profile> CreateProfileFromHistoryRecord(HistoryRecord historyRecord, CancellationToken ctk)
    {
        return historyRecord.Type switch
        {
            ProfileType.Text => Task.FromResult<Profile>(new TextProfile(historyRecord.Text)),
            ProfileType.File => Task.FromResult<Profile>(new FileProfile(historyRecord)),
            ProfileType.Image => Task.FromResult<Profile>(new ImageProfile(historyRecord)),
            ProfileType.Group => Task.FromResult<Profile>(new GroupProfile(historyRecord)),
            _ => Task.FromResult<Profile>(new UnknownProfile()),
        };
    }

    public async Task<Profile> CreateProfileFromLocal(CancellationToken ctk)
    {
        var meta = await GetMetaInfomation(ctk);
        return await CreateProfileFromMeta(meta, ctk);
    }

    public static Profile GetProfileBy(ClipboardProfileDTO profileDTO)
    {
        switch (profileDTO.Type)
        {
            case ProfileType.Text:
                return new TextProfile(profileDTO.Clipboard);
            case ProfileType.File:
                {
                    if (ImageHelper.FileIsImage(profileDTO.File))
                    {
                        return new ImageProfile(profileDTO);
                    }
                    return new FileProfile(profileDTO);
                }
            case ProfileType.Image:
                return new ImageProfile(profileDTO);
            case ProfileType.Group:
                return new GroupProfile(profileDTO);
        }

        return new UnknownProfile();
    }
}
