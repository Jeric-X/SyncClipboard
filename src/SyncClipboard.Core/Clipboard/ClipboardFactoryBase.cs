using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
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

    public Profile CreateProfileFromMeta(ClipboardMetaInfomation metaInfomation)
    {
        if (metaInfomation.Files != null)
        {
            var filename = metaInfomation.Files[0];
            if (File.Exists(filename))
            {
                if (ImageHelper.FileIsImage(filename))
                {
                    return new ImageProfile(filename, ServiceProvider);
                }
                return new FileProfile(filename, ServiceProvider);
            }
        }

        if (metaInfomation.Image != null)
        {
            return new ImageProfile(metaInfomation.Image, ServiceProvider);
        }

        if (metaInfomation.Text != null)
        {
            return new TextProfile(metaInfomation.Text, ServiceProvider);
        }

        return new UnkonwnProfile();
    }

    private async Task<Profile> UploadAndReturnBlankProfile(CancellationToken ctk)
    {
        var blankProfile = new TextProfile("", ServiceProvider);
        await blankProfile.UploadProfile(WebDav, ctk);
        return blankProfile;
    }

    public async Task<Profile> CreateProfileFromLocal(CancellationToken ctk)
    {
        var meta = await GetMetaInfomation(ctk);
        return CreateProfileFromMeta(meta);
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
        catch
        {
            Logger.Write("CreateFromRemote failed");
            throw;
        }
    }

    private Profile GetProfileBy(ClipboardProfileDTO profileDTO)
    {
        switch (profileDTO.Type)
        {
            case ProfileType.Text:
                return new TextProfile(profileDTO.Clipboard, ServiceProvider);
            case ProfileType.File:
                {
                    if (ImageHelper.FileIsImage(profileDTO.File))
                    {
                        return new ImageProfile(profileDTO, ServiceProvider);
                    }
                    return new FileProfile(profileDTO, ServiceProvider);
                }
            case ProfileType.Image:
                return new ImageProfile(profileDTO, ServiceProvider);
        }

        return new UnkonwnProfile();
    }

    public async Task<(ClipboardProfileDTO, string?)> CreateProfileDto(CancellationToken ctk)
    {
        string? extraFilePath = null;
        var profile = await CreateProfileFromLocal(ctk);
        if (profile is FileProfile fileProfile)
        {
            await fileProfile.CalcFileHash(ctk);
            extraFilePath = fileProfile.FullPath;
        }
        return (profile.ToDto(), extraFilePath);
    }

    public async Task SetLocalClipboardWithDto(ClipboardProfileDTO profileDto, string fileFolder)
    {
        ArgumentNullException.ThrowIfNull(profileDto);
        var profile = GetProfileBy(profileDto);
        if (profile is FileProfile fileProfile)
        {
            fileProfile.FullPath = Path.Combine(fileFolder, fileProfile.FileName);
        }

        try
        {
            var ctk = new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;
            if (!await Profile.Same(profile, await CreateProfileFromLocal(ctk), ctk))
            {
                profile.SetLocalClipboard(true, ctk);
                Logger.Write("Set clipboard with: " + profileDto.ToString().Replace(Environment.NewLine, @"\n"));
            }
        }
        catch (TaskCanceledException)
        {
            Logger.Write("Set local clipboard timeout.");
        }
    }
}
