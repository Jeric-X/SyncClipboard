using SyncClipboard.Abstract;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.Image;

namespace SyncClipboard.Core.Clipboard;

public abstract class ClipboardFactoryBase : IClipboardFactory
{
    protected abstract ILogger Logger { get; set; }
    protected abstract IServiceProvider ServiceProvider { get; set; }

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
                    return await ImageProfile.Create(filename, contentControl, ctk);
                }
                return await FileProfile.Create(filename, contentControl, ctk);
            }
            else
            {
                return await GroupProfile.Create(metaInfomation.Files, contentControl, ctk);
            }
        }

        if (metaInfomation.Text != null)
        {
            return new TextProfile(metaInfomation.Text, contentControl);
        }

        if (metaInfomation.Image != null)
        {
            return await ImageProfile.Create(metaInfomation.Image, contentControl, ctk);
        }

        return new UnknownProfile();
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
