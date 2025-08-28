using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Image;
using System.Net;
using System.Text.Json;

namespace SyncClipboard.Core.Clipboard;

public abstract class ClipboardFactoryBase : IClipboardFactory, IProfileDtoHelper
{
    protected abstract ILogger Logger { get; set; }
    protected abstract IServiceProvider ServiceProvider { get; set; }
    protected abstract IWebDav WebDav { get; set; }

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

    public async Task<Profile> CreateProfileFromHistoryRecord(HistoryRecord historyRecord, CancellationToken ctk)
    {
        switch (historyRecord.Type)
        {
            case ProfileType.Text:
                return new TextProfile(historyRecord.Text);
            case ProfileType.File:
                {
                    if (ImageHelper.FileIsImage(historyRecord.FilePath[0]))
                    {
                        return await ImageProfile.Create(historyRecord.FilePath[0], ctk);
                    }
                    return await FileProfile.Create(historyRecord.FilePath[0], ctk);
                }
            case ProfileType.Image:
                return await ImageProfile.Create(historyRecord.FilePath[0], ctk);
            case ProfileType.Group:
                return await GroupProfile.Create(historyRecord.FilePath, ctk);
        }

        return new UnknownProfile();
    }

    private async Task<Profile> UploadAndReturnBlankProfile(CancellationToken ctk)
    {
        var blankProfile = new TextProfile("");
        await blankProfile.UploadProfile(WebDav, ctk);
        return blankProfile;
    }

    public async Task<Profile> CreateProfileFromLocal(CancellationToken ctk)
    {
        var meta = await GetMetaInfomation(ctk);
        return await CreateProfileFromMeta(meta, ctk);
    }

    public async Task<Profile> CreateProfileFromRemote(CancellationToken cancelToken)
    {
        try
        {
            var profileDTO = await WebDav.GetJson<ClipboardProfileDTO>(Env.RemoteProfilePath, cancelToken);
            Logger.Write(nameof(ClipboardFactoryBase), profileDTO?.ToString() ?? "null");
            ArgumentNullException.ThrowIfNull(profileDTO);

            return GetProfileBy(profileDTO);
        }
        catch (Exception ex) when (
            ex is JsonException ||
            ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound } ||
            ex is ArgumentException)
        {
            return await UploadAndReturnBlankProfile(cancelToken);
        }
        catch (Exception ex)
        {
            Logger.Write($"CreateFromRemote failed {ex}");
            throw;
        }
    }

    private static Profile GetProfileBy(ClipboardProfileDTO profileDTO)
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

    public async Task<ClipboardProfileDTO> CreateProfileDto(string destFolder)
    {
        var token = CancellationToken.None;
        var meta = await GetMetaInfomation(token);
        var profile = await CreateProfileFromMeta(meta, token);

        bool doNotUploadWhenCut = AppCore.Current.ConfigManager.GetConfig<SyncConfig>().DoNotUploadWhenCut;
        bool isCut = (meta.Effects & DragDropEffects.Move) == DragDropEffects.Move;
        if ((doNotUploadWhenCut && isCut) || !profile.IsAvailableFromLocal())
        {
            return new UnknownProfile().ToDto();
        }

        if (profile is FileProfile fileProfile)
        {
            if (fileProfile is GroupProfile groupProfile)
            {
                await groupProfile.PrepareTransferFile(token);
            }
            var fullPath = fileProfile.FullPath!;
            if (Path.GetDirectoryName(fullPath) != destFolder)
            {
                if (!Directory.Exists(destFolder))
                {
                    Directory.CreateDirectory(destFolder);
                }
                await Task.Run(() => File.Copy(fullPath, Path.Combine(destFolder, Path.GetFileName(fullPath)), true));
            }
        }
        return profile.ToDto();
    }

    public async Task SetLocalClipboardWithDto(ClipboardProfileDTO profileDto, string fileFolder)
    {
        ArgumentNullException.ThrowIfNull(profileDto);
        try
        {
            var ctk = new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;
            var profile = GetProfileBy(profileDto);
            if (profile is FileProfile fileProfile)
            {
                fileProfile.FullPath = Path.Combine(fileFolder, fileProfile.FileName);
                if (profile is GroupProfile groupProfile)
                {
                    await groupProfile.ExtractFiles(ctk);
                }
            }

            if (!Profile.Same(profile, await CreateProfileFromLocal(ctk)))
            {
                await profile.SetLocalClipboard(true, ctk);
                Logger.Write("Set clipboard with: " + profileDto.ToString().Replace(Environment.NewLine, @"\n"));
            }
        }
        catch (TaskCanceledException)
        {
            Logger.Write("Set local clipboard timeout.");
        }
    }
}
